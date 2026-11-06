using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Server.Tasks;
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
using Microsoft.Extensions.Options;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// The main loop (blueprint §5): one feed request per indexer, every new release parsed, matched to a film of the
/// library and judged by each of its versions with the automatic rules, attributed to one version, and the best
/// release of the pass for each version sent to a download client. This is what fills the library day to day.
/// </summary>
public sealed class RssSyncService(
    BigarrDbContext db,
    TargetContextFactory contexts,
    CustomFormatScorer scorer,
    IndexerGateway gateway,
    GrabService grabs,
    RssFeedState state,
    SchedulerState scheduler,
    IOptions<RssSyncOptions> options,
    IOptions<SchedulerOptions> schedulerOptions,
    TimeProvider time,
    ILogger<RssSyncService> logger)
{
    public const string GrabTrigger = "rss";

    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Automatic();

    /// <summary>A next pass only when one will come on its own: RSS enabled and the scheduler on, as System → Tasks shows it.</summary>
    public RssStatusResponse Status()
    {
        var scheduling = schedulerOptions.Value.Enabled;
        var nextRunAt = options.Value.Enabled && scheduling ? scheduler.NextRunAt(RssSyncJob.JobName) : null;
        return state.Status(options.Value, scheduling, nextRunAt);
    }

    public async Task<Result<RssSyncResponse>> RunAsync(CancellationToken ct)
    {
        if (!state.TryBeginRun())
        {
            return Failure.Conflict("RSS sync already running", "Wait for the current pass to finish.");
        }

        try
        {
            return Result<RssSyncResponse>.Ok(await RunCoreAsync(ct));
        }
        finally
        {
            state.EndRun(time.GetUtcNow().UtcDateTime);
        }
    }

    private async Task<RssSyncResponse> RunCoreAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        state.Prune(now);

        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableRss).OrderBy(i => i.Priority).ToListAsync(ct);
        if (indexers.Count == 0)
        {
            logger.LogDebug("RSS sync: no indexer has RSS enabled");
            return new RssSyncResponse(0, 0, 0, 0, 0, []);
        }

        var results = await IndexerQueries.RunAsync(indexers, (indexer, token) => gateway.FetchRecentAsync(indexer, token), ct);

        var fresh = new List<(Indexer Indexer, ReleaseInfo Release)>();
        foreach (var result in results)
        {
            var newCount = 0;
            foreach (var release in result.Releases)
            {
                if (state.MarkSeen(result.Indexer.Id, release.Guid, now))
                {
                    fresh.Add((result.Indexer, release));
                    newCount++;
                }
            }

            state.RecordIndexer(new RssIndexerStatusResponse(result.Indexer.Id, result.Indexer.Name, now, result.Error?.Message, result.Releases.Count, newCount));
        }

        var matched = 0;
        var wouldGrab = 0;
        var grabbed = 0;
        if (fresh.Count > 0)
        {
            var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
            var library = LibraryIndex.Build(movies);
            var targets = await contexts.BuildAsync(movies, ct);
            var calculator = await scorer.CalculatorAsync(ct);

            // Oldest first so the newest ends up on top of the list.
            var judged = new List<(RssReleaseResponse Entry, ReleaseInfo Release, int Score)>();
            foreach (var (indexer, release) in fresh.OrderBy(f => f.Release.PublishDate))
            {
                var (entry, score) = Judge(indexer, release, library, targets, calculator, now);
                if (entry.Movie is not null)
                {
                    matched++;
                }

                if (entry.AttributedVersionId is not null)
                {
                    wouldGrab++;
                }

                judged.Add((entry, release, score));
            }

            // One grab per version and pass: the best of what the feed brought (blueprint §5 ranking).
            var winners = new Dictionary<int, (RssReleaseResponse Entry, ReleaseInfo Release)>();
            foreach (var group in judged.Where(j => j.Entry.AttributedVersionId is not null).GroupBy(j => j.Entry.AttributedVersionId!.Value))
            {
                var best = ReleaseRanking.Order(group, j => j.Entry.QualityId, j => j.Score, j => j.Entry.RevisionVersion, j => j.Release.IndexerPriority, j => j.Release.Seeders ?? 0).First();
                winners[group.Key] = (best.Entry, best.Release);
            }

            var outcomes = new Dictionary<string, (int? GrabId, string? Error)>(StringComparer.Ordinal);
            foreach (var (versionId, (entry, release)) in winners)
            {
                var grab = await grabs.GrabAsync(GrabRequest.From(release, versionId), GrabTrigger, ct);
                if (grab.IsSuccess)
                {
                    grabbed++;
                    outcomes[Key(entry)] = (grab.Value!.GrabId, null);
                }
                else
                {
                    var failure = grab.Failure!;
                    logger.LogWarning("RSS: {Title} not grabbed for version {Version}: {Title2} {Detail}", entry.Title, versionId, failure.Title, failure.Detail);
                    outcomes[Key(entry)] = (null, failure.Detail is null ? failure.Title : $"{failure.Title}: {failure.Detail}");
                }
            }

            foreach (var (entry, _, _) in judged)
            {
                var stored = entry;
                if (entry.AttributedVersionId is not null)
                {
                    stored = outcomes.TryGetValue(Key(entry), out var outcome)
                        ? entry with { GrabId = outcome.GrabId, GrabError = outcome.Error }
                        : entry with { GrabError = $"A better release of this pass was grabbed for {entry.AttributedVersionName}" };
                }

                state.Add(stored);
            }
        }

        var errors = results.Where(r => r.Error is not null)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        logger.LogInformation("RSS sync: {Indexers} indexer(s), {Fetched} release(s), {New} new, {Matched} matched, {WouldGrab} attributed, {Grabbed} grabbed, {Errors} error(s)",
            indexers.Count, results.Sum(r => r.Releases.Count), fresh.Count, matched, wouldGrab, grabbed, errors.Count);

        return new RssSyncResponse(indexers.Count, results.Sum(r => r.Releases.Count), fresh.Count, matched, wouldGrab, errors, grabbed);
    }

    private static string Key(RssReleaseResponse entry) => $"{entry.IndexerId}:{entry.Guid}";

    private (RssReleaseResponse Entry, int Score) Judge(Indexer indexer, ReleaseInfo release, LibraryIndex library, Dictionary<int, List<TargetContext>> targets, CustomFormatCalculator calculator, DateTime now)
    {
        var parsed = ReleaseMatcher.TryParse(release.Title);
        var (movie, reason) = ReleaseMatcher.FindMovie(parsed, release, library);
        var formats = parsed is null ? [] : calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie?.Year, movie?.OriginalLanguage));

        var versions = new List<VersionDecisionResponse>();
        ReleaseDecision? attributed = null;
        if (movie is not null && parsed is not null)
        {
            var candidate = new ReleaseCandidate(release, parsed, indexer.MinimumSeeders) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
            var decisions = targets[movie.Id].Select(context => _evaluator.Evaluate(candidate, context)).ToList();
            attributed = VersionAttribution.Choose(decisions);
            versions = decisions.Select(d =>
            {
                var version = (MovieVersion)d.Target.Target;
                return new VersionDecisionResponse(version.Id, version.Name, d.Approved, d.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList(), d.CustomFormatScore);
            }).ToList();
        }

        var quality = parsed?.Quality?.Quality ?? ParserQuality.Unknown;
        var revision = parsed?.Quality?.Revision;
        var attributedVersion = attributed is null ? null : (MovieVersion)attributed.Target.Target;

        return (new RssReleaseResponse(
            release.Guid,
            release.Title,
            release.IndexerId,
            release.IndexerName,
            release.Size,
            release.Seeders,
            release.Leechers,
            release.PublishDate,
            now,
            release.InfoUrl,
            release.Flags,
            quality.Id,
            quality.Name,
            revision?.Version ?? 1,
            revision?.IsRepack ?? false,
            string.IsNullOrWhiteSpace(parsed?.Edition) ? null : parsed!.Edition,
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            parsed?.Languages?.Where(l => l.Id != 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
            formats.Select(f => f.Name).ToList(),
            parsed?.PrimaryMovieTitle,
            parsed is { Year: > 0 } ? parsed.Year : null,
            movie is null ? null : new RssMovieRef(movie.Id, movie.Title, movie.Year, movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl),
            reason,
            versions,
            attributedVersion?.Id,
            attributedVersion?.Name), attributed?.CustomFormatScore ?? 0);
    }
}

/// <summary>The RSS pass as a scheduled task (blueprint §3.7): interval and grace period from <see cref="RssSyncOptions"/>.</summary>
public sealed class RssSyncJob(RssSyncService rss, IOptions<RssSyncOptions> options) : IScheduledJob
{
    public const string JobName = "rssSync";

    public string Name => JobName;

    public string DisplayName => "RSS sync";

    public TimeSpan Interval => options.Value.Interval;

    public bool Enabled => options.Value.Enabled;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(Math.Max(0, options.Value.InitialDelaySeconds));

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var result = await rss.RunAsync(ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.Title;
        }

        var pass = result.Value!;
        return pass.Indexers == 0
            ? "No indexer has RSS enabled"
            : $"{pass.Fetched} release(s) from {pass.Indexers} indexer(s), {pass.New} new, {pass.Matched} matched, {pass.Grabbed} grabbed" + (pass.IndexerErrors.Count > 0 ? $", {pass.IndexerErrors.Count} indexer error(s)" : string.Empty);
    }
}
