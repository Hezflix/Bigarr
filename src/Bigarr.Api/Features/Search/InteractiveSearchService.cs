using System.Diagnostics;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Interactive search (blueprint §5): every indexer in parallel, each release parsed, matched to the film, then judged by
/// every version of the film so the user sees the reasons, not just the verdict. Grabbing arrives with the download client.
/// </summary>
public sealed class InteractiveSearchService(BigarrDbContext db, IndexerGateway gateway, TimeProvider time, ILogger<InteractiveSearchService> logger)
{
    private static readonly TimeSpan PerIndexerTimeout = TimeSpan.FromSeconds(60);

    private static readonly Dictionary<int, int> QualityWeights = ParserQuality.DefaultQualityDefinitions.ToDictionary(d => d.Quality.Id, d => d.Weight);

    private readonly ReleaseEvaluator _evaluator = new();

    public async Task<Result<SearchResponse>> SearchAsync(int movieId, int? versionId, CancellationToken ct)
    {
        var movie = await db.Movies.AsNoTracking()
            .Include(m => m.Versions).ThenInclude(v => v.QualityProfile)
            .Include(m => m.Versions).ThenInclude(v => v.File)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == movieId, ct);
        if (movie is null)
        {
            return Failure.NotFound("Movie");
        }

        var versions = movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToList();
        if (versionId is not null)
        {
            versions = versions.Where(v => v.Id == versionId).ToList();
            if (versions.Count == 0)
            {
                return Failure.NotFound("Version");
            }
        }

        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableInteractiveSearch).OrderBy(i => i.Priority).ToListAsync(ct);
        if (indexers.Count == 0)
        {
            return Failure.Invalid("No indexer available", "No indexer has interactive search enabled. Add one in Settings → Indexers.");
        }

        var criteria = new MovieSearchCriteria(
            movie.Title,
            movie.Year,
            movie.ImdbId,
            movie.TmdbId,
            new[] { movie.OriginalTitle }.Concat(movie.AlternativeTitles.Select(a => a.Title)).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct().ToList());

        var stopwatch = Stopwatch.StartNew();
        var results = await Task.WhenAll(indexers.Select(indexer => QueryAsync(indexer, criteria, ct)));
        logger.LogInformation("Interactive search for {Movie}: {Releases} release(s) from {Indexers} indexer(s) in {Elapsed} ms",
            movie.Title, results.Sum(r => r.Releases.Count), indexers.Count, stopwatch.ElapsedMilliseconds);

        var contexts = await BuildContextsAsync(movie, versions, ct);
        var minimumSeeders = indexers.ToDictionary(i => i.Id, i => i.MinimumSeeders);

        var releases = results
            .SelectMany(r => r.Releases)
            .Select(release => Evaluate(release, movie, contexts, minimumSeeders.GetValueOrDefault(release.IndexerId)))
            .OrderByDescending(r => r.MatchesMovie)
            .ThenByDescending(r => r.Versions.Any(v => v.Approved))
            .ThenByDescending(r => QualityWeights.GetValueOrDefault(r.QualityId))
            .ThenByDescending(r => r.RevisionVersion)
            .ThenBy(r => r.IndexerPriority)
            .ThenByDescending(r => r.Seeders ?? 0)
            .ToList();

        var errors = results.Where(r => r.Error is not null)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        return Result<SearchResponse>.Ok(new SearchResponse(movie.Id, time.GetUtcNow().UtcDateTime, indexers.Count, releases, errors));
    }

    private async Task<(Indexer Indexer, IReadOnlyList<ReleaseInfo> Releases, IndexerException? Error)> QueryAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PerIndexerTimeout);
        try
        {
            return (indexer, await gateway.SearchAsync(indexer, criteria, timeout.Token), null);
        }
        catch (IndexerException ex)
        {
            return (indexer, [], ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (indexer, [], new IndexerException(IndexerFailureKind.Unreachable, $"No answer within {PerIndexerTimeout.TotalSeconds:0} seconds."));
        }
    }

    /// <summary>One <see cref="TargetContext"/> per version; one-shot versions borrow the default version's blocklist (§3.5).</summary>
    private async Task<List<TargetContext>> BuildContextsAsync(Movie movie, List<MovieVersion> versions, CancellationToken ct)
    {
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var defaultVersionId = movie.Versions.FirstOrDefault(v => v.IsDefault)?.Id;
        var targetIds = versions.Select(v => v.Id).Concat(defaultVersionId is null ? [] : [defaultVersionId.Value]).Distinct().ToList();

        var blocklist = await db.Blocklist.AsNoTracking().Where(b => targetIds.Contains(b.TargetId)).ToListAsync(ct);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active && targetIds.Contains(t.TargetId)).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();

        return versions.Select(version =>
        {
            var blocklistOwner = version.Mode == VersionMode.OneShot && defaultVersionId is not null ? defaultVersionId.Value : version.Id;
            return new TargetContext(
                version,
                version.QualityProfile,
                definitions,
                version.File is null ? null : new ExistingFile(version.File.QualityId, version.File.Revision.Version, version.File.Revision.Real, version.File.Revision.IsRepack),
                movie.Runtime,
                blocklist.Where(b => b.TargetId == blocklistOwner).ToList(),
                active.Contains(version.Id));
        }).ToList();
    }

    private ReleaseResponse Evaluate(ReleaseInfo release, Movie movie, List<TargetContext> contexts, int minimumSeeders)
    {
        var parsed = ReleaseMatcher.TryParse(release.Title);
        var mismatch = ReleaseMatcher.Match(parsed, release, movie);

        var versions = new List<VersionDecisionResponse>();
        if (mismatch is null && parsed is not null)
        {
            var candidate = new ReleaseCandidate(release, parsed, minimumSeeders);
            foreach (var context in contexts)
            {
                var version = (MovieVersion)context.Target;
                var decision = _evaluator.Evaluate(candidate, context);
                versions.Add(new VersionDecisionResponse(
                    version.Id,
                    version.Name,
                    decision.Approved,
                    decision.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList()));
            }
        }

        var quality = parsed?.Quality?.Quality ?? ParserQuality.Unknown;
        var revision = parsed?.Quality?.Revision;

        return new ReleaseResponse(
            release.Guid,
            release.Title,
            release.IndexerId,
            release.IndexerName,
            release.IndexerPriority,
            release.Protocol,
            release.Size,
            release.Seeders,
            release.Leechers,
            release.PublishDate,
            release.DownloadUrl,
            release.MagnetUrl,
            release.InfoUrl,
            release.InfoHash,
            release.Flags,
            quality.Id,
            quality.Name,
            quality.Resolution,
            revision?.Version ?? 1,
            revision?.IsRepack ?? false,
            string.IsNullOrWhiteSpace(parsed?.Edition) ? null : parsed!.Edition,
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            parsed?.Languages?.Where(l => l.Id != 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [], // Unknown is the parser's placeholder, not a language
            parsed?.PrimaryMovieTitle,
            parsed is { Year: > 0 } ? parsed.Year : null,
            mismatch is null,
            mismatch is null ? [] : [new RejectionResponse(mismatch, false)],
            versions);
    }
}
