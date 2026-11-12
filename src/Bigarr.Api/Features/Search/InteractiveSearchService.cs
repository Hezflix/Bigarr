using System.Diagnostics;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Interactive search: every indexer in parallel, each release parsed, matched to the film, then judged by
/// every version of the film so the user sees the reasons, not just the verdict. The search is for the film: the version
/// a release goes to is chosen when grabbing, each release carries a suggested one.
/// </summary>
public sealed class InteractiveSearchService(BigarrDbContext db, TargetContextFactory contexts, CustomFormatScorer scorer, IndexerGateway gateway, TimeProvider time, ILogger<InteractiveSearchService> logger)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Interactive();

    /// <summary>Searches the film by its ids and titles, or by <paramref name="query"/> when the user typed one.</summary>
    public async Task<Result<SearchResponse>> SearchAsync(int movieId, int? versionId, string? query, CancellationToken ct)
    {
        var movie = await contexts.MoviesWithVersions().FirstOrDefaultAsync(m => m.Id == movieId, ct);
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

        // The user's own words go out as they are: no ids, no year, no alternative titles, so every indexer gets a text query.
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var criteria = query is null ? CriteriaFor(movie) : MovieSearchCriteria.ForTitle(query);

        var stopwatch = Stopwatch.StartNew();
        var results = await IndexerQueries.RunAsync(indexers, (indexer, token) => gateway.SearchAsync(indexer, criteria, token), ct);
        logger.LogInformation("Interactive search for {Movie}{Query}: {Releases} release(s) from {Indexers} indexer(s) in {Elapsed} ms",
            movie.Title, query is null ? string.Empty : $" by text '{query}'", results.Sum(r => r.Releases.Count), indexers.Count, stopwatch.ElapsedMilliseconds);

        var targets = await contexts.BuildForGrabAsync(movie, versions, ct);
        var calculator = await scorer.CalculatorAsync(ct);
        var minimumSeeders = indexers.ToDictionary(i => i.Id, i => i.MinimumSeeders);

        // Ranking: quality, then custom format score, then indexer priority, then seeders.
        var releases = results
            .SelectMany(r => r.Releases)
            .Select(release => Evaluate(release, movie, targets, minimumSeeders.GetValueOrDefault(release.IndexerId), calculator))
            .OrderByDescending(r => r.MatchesMovie)
            .ThenByDescending(r => r.Versions.Any(v => v.Approved))
            .ThenByDescending(r => ReleaseRanking.WeightOf(r.QualityId))
            .ThenByDescending(r => r.Versions.Count == 0 ? 0 : r.Versions.Max(v => v.CustomFormatScore))
            .ThenByDescending(r => r.RevisionVersion)
            .ThenBy(r => r.IndexerPriority)
            .ThenByDescending(r => r.Seeders ?? 0)
            .ToList();

        var errors = results.Where(r => r.Error is not null)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        return Result<SearchResponse>.Ok(new SearchResponse(movie.Id, time.GetUtcNow().UtcDateTime, indexers.Count, releases, errors, query));
    }

    /// <summary>What the indexers are asked: title, year, ids and every alternative title the metadata knows.</summary>
    public static MovieSearchCriteria CriteriaFor(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new MovieSearchCriteria(
            movie.Title,
            movie.Year,
            movie.ImdbId,
            movie.TmdbId,
            new[] { movie.OriginalTitle }.Concat(movie.AlternativeTitles.Select(a => a.Title)).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct().ToList());
    }

    private ReleaseResponse Evaluate(ReleaseInfo release, Movie movie, List<TargetContext> contexts, int minimumSeeders, CustomFormatCalculator calculator)
    {
        var parsed = ReleaseMatcher.TryParse(release.Title);
        var mismatch = ReleaseMatcher.Match(parsed, release, movie);
        var formats = parsed is null ? [] : calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie.Year, movie.OriginalLanguage));

        // A release that is not the film is still judged: the user may know better (a film listed under another name)
        // and grab it by hand, and then wants to know which version it fits.
        var decisions = new List<ReleaseDecision>();
        if (parsed is not null)
        {
            var candidate = new ReleaseCandidate(release, parsed, minimumSeeders) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
            decisions.AddRange(contexts.Select(context => _evaluator.Evaluate(candidate, context)));
        }

        var versions = decisions
            .Select(decision => new VersionDecisionResponse(
                decision.Target.Target.Id,
                ((MovieVersion)decision.Target.Target).Name,
                decision.Approved,
                decision.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList(),
                decision.CustomFormatScore))
            .ToList();
        var suggested = VersionAttribution.Suggest(decisions)?.Target.Target.Id ?? contexts.FirstOrDefault()?.Target.Id;

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
            formats.Select(f => f.Name).ToList(),
            parsed?.PrimaryMovieTitle,
            parsed is { Year: > 0 } ? parsed.Year : null,
            mismatch is null,
            mismatch is null ? [] : [new RejectionResponse(mismatch, false)],
            versions,
            suggested);
    }
}
