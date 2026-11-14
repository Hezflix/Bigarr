using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Decision.Specifications;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// The main loop: one feed request per indexer, every release of the feed parsed, matched to a film of
/// the library and judged by each of its versions with the automatic rules, attributed to one version, and the best
/// release of the pass for each version sent to a download client. This is what fills the library day to day. Like
/// Radarr, a pass judges the whole feed again, not only what is new: a release turned down while its film was not
/// available yet, unmonitored or had another file is taken as soon as that changes, as long as the feed still carries
/// it. In shadow mode the winner is recorded as a pick instead, and every release is kept once as a sighting for the
/// comparison.
/// </summary>
public sealed class RssSyncService(
    BigarrDbContext db,
    TargetContextFactory contexts,
    CustomFormatScorer scorer,
    IndexerGateway gateway,
    GrabService grabs,
    ReleaseSightings sightings,
    SeriesRssPass seriesRss,
    RssFeedState state,
    SchedulerState scheduler,
    IOptions<RssSyncOptions> options,
    IOptions<SchedulerOptions> schedulerOptions,
    TimeProvider time,
    ILogger<RssSyncService> logger)
{
    public const string GrabTrigger = "rss";

    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Rss(time);

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
            // A whole pass reads every feed a pending recheck would: it replaces it, and plans its own if needed.
            CancelRecheck();
            var (response, waiting) = await RunCoreAsync(null, ct);
            logger.LogInformation("RSS sync: {Indexers} indexer(s), {Fetched} release(s), {New} new, {Matched} matched, {WouldGrab} attributed, {Grabbed} grabbed, {Picked} picked in shadow mode, {Errors} error(s)",
                response.Indexers, response.Fetched, response.New, response.Matched, response.WouldGrab, response.Grabbed, response.Picked, response.IndexerErrors.Count);
            PlanRecheck(waiting);
            return Result<RssSyncResponse>.Ok(response);
        }
        finally
        {
            state.EndRun(time.GetUtcNow().UtcDateTime);
        }
    }

    /// <summary>
    /// The quick recheck a pass planned: only the feeds that held a release waiting for seeders, judged like any pass
    /// (sightings, grabs or picks), and never a recheck of its own. Null when there was nothing to recheck: no plan,
    /// or a whole pass running, which reads these feeds anyway.
    /// </summary>
    public async Task<RssRecheckOutcome?> RecheckAsync(CancellationToken ct)
    {
        if (state.PendingRecheck is null || !state.TryBeginRun())
        {
            return null;
        }

        try
        {
            var plan = state.TakeRecheck();
            if (plan is null)
            {
                return null; // a whole pass took it in between
            }

            scheduler.For(RssRecheckJob.JobName).NextRunAt = null; // done now, even when run by hand ahead of time

            var (response, _) = await RunCoreAsync(plan, ct);
            var ids = plan.Indexers.Keys.ToList();
            var names = await db.Indexers.AsNoTracking().Where(i => ids.Contains(i.Id)).OrderBy(i => i.Name).Select(i => i.Name).ToListAsync(ct);
            logger.LogInformation("RSS quick recheck: {Waiting} release(s) waiting for seeders on {Names}: {Fetched} release(s) read, {Matched} matched, {Grabbed} grabbed, {Picked} picked in shadow mode, {Errors} error(s)",
                plan.Waiting, string.Join(", ", names), response.Fetched, response.Matched, response.Grabbed, response.Picked, response.IndexerErrors.Count);
            return new RssRecheckOutcome(plan.Waiting, names, response);
        }
        finally
        {
            state.EndRun(time.GetUtcNow().UtcDateTime, full: false);
        }
    }

    private void CancelRecheck()
    {
        state.PlanRecheck(null);
        scheduler.For(RssRecheckJob.JobName).NextRunAt = null;
    }

    /// <summary>One quick recheck of the feeds that held a release waiting for seeders, when the setting allows it.</summary>
    private void PlanRecheck(List<(int IndexerId, bool Series)> waiting)
    {
        if (waiting.Count == 0 || options.Value.QuickRecheck is not { } delay)
        {
            return;
        }

        var dueAt = time.GetUtcNow().UtcDateTime + delay;
        var indexers = waiting.GroupBy(w => w.IndexerId).ToDictionary(g => g.Key, g => new RssRecheckFeeds(g.Any(w => !w.Series), g.Any(w => w.Series)));
        state.PlanRecheck(new RssRecheckPlan(indexers, waiting.Count, dueAt));
        scheduler.For(RssRecheckJob.JobName).NextRunAt = dueAt;
        logger.LogDebug("RSS: {Waiting} release(s) waiting for seeders, quick recheck of {Indexers} indexer(s) at {DueAt:u}", waiting.Count, indexers.Count, dueAt);
    }

    /// <param name="recheck">Null for a whole pass; otherwise only the planned feeds of the planned indexers.</param>
    private async Task<(RssSyncResponse Response, List<(int IndexerId, bool Series)> Waiting)> RunCoreAsync(RssRecheckPlan? recheck, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        state.Prune(now);

        var query = db.Indexers.AsNoTracking().Where(i => i.EnableRss);
        if (recheck is not null)
        {
            var ids = recheck.Indexers.Keys.ToList();
            query = query.Where(i => ids.Contains(i.Id));
        }

        var indexers = await query.OrderBy(i => i.Priority).ToListAsync(ct);
        var waiting = new List<(int IndexerId, bool Series)>();
        if (indexers.Count == 0)
        {
            logger.LogDebug("RSS sync: no indexer has RSS enabled");
            return (new RssSyncResponse(0, 0, 0, 0, 0, []), waiting);
        }

        var filmIndexers = recheck is null ? indexers : indexers.Where(i => recheck.Indexers[i.Id].Films).ToList();
        var results = filmIndexers.Count > 0
            ? await IndexerQueries.RunAsync(filmIndexers, (indexer, token) => gateway.FetchRecentAsync(indexer, token), ct)
            : [];

        // Series come from the indexer's TV and anime categories: a second feed request per indexer, only once the
        // library holds a series, so a films-only library asks exactly what it asked before. A quick recheck reads
        // only the feeds that held a waiting release.
        var seriesIndexers = recheck is not null ? indexers.Where(i => recheck.Indexers[i.Id].Series).ToList()
            : await db.Series.AnyAsync(ct) ? indexers
            : [];
        var seriesResults = seriesIndexers.Count > 0
            ? await IndexerQueries.RunAsync(seriesIndexers, (indexer, token) => gateway.FetchRecentSeriesAsync(indexer, token), ct)
            : [];

        // Every release of the feed is judged; "new" only says what this pass brought for the first time.
        var feed = new List<(Indexer Indexer, ReleaseInfo Release)>();
        var seriesFeed = new List<(Indexer Indexer, ReleaseInfo Release)>();
        // A release is judged once per feed it comes from: a release a film feed and a series feed both list is judged
        // as a film and as an episode, and the sighting keeps what each judgement found.
        var seenFilms = new HashSet<string>(StringComparer.Ordinal);
        var seenSeries = new HashSet<string>(StringComparer.Ordinal);
        var fresh = 0;
        foreach (var indexer in indexers)
        {
            var result = results.FirstOrDefault(r => r.Indexer.Id == indexer.Id);
            var seriesResult = seriesResults.FirstOrDefault(r => r.Indexer.Id == indexer.Id);
            var newCount = 0;
            foreach (var (release, isSeries) in (result?.Releases ?? []).Select(r => (r, false)).Concat((seriesResult?.Releases ?? []).Select(r => (r, true))))
            {
                if (!(isSeries ? seenSeries : seenFilms).Add($"{indexer.Id}:{release.Guid}"))
                {
                    continue; // a feed that lists a release twice
                }

                (isSeries ? seriesFeed : feed).Add((indexer, release));
                if (state.MarkSeen(indexer.Id, release.Guid, now))
                {
                    newCount++; // counted once, whatever number of feeds list it
                }
            }

            fresh += newCount;

            var error = result?.Error?.Message ?? seriesResult?.Error?.Message;
            state.RecordIndexer(new RssIndexerStatusResponse(indexer.Id, indexer.Name, now, error, (result?.Releases.Count ?? 0) + (seriesResult?.Releases.Count ?? 0), newCount));
        }

        var matched = 0;
        var wouldGrab = 0;
        var grabbed = 0;
        var picked = 0;
        var matchedAsFilm = new HashSet<string>(StringComparer.Ordinal);
        if (feed.Count > 0)
        {
            var movies = await contexts.MoviesWithVersions().ToListAsync(ct);
            var library = LibraryIndex.Build(movies);
            var targets = await contexts.BuildForGrabAsync(movies, now, ct);
            var calculator = await scorer.CalculatorAsync(ct);

            // Oldest first so the newest ends up on top of the list.
            var judged = new List<(RssReleaseResponse Entry, ReleaseInfo Release, int Score, List<ReleaseDecision> Decisions)>();
            foreach (var (indexer, release) in feed.OrderBy(f => f.Release.PublishDate))
            {
                var (entry, score, decisions) = Judge(indexer, release, library, targets, calculator, now);
                if (entry.Movie is not null)
                {
                    matched++;
                    matchedAsFilm.Add(Key(entry));
                }

                if (entry.AttributedVersionId is not null)
                {
                    wouldGrab++;
                }

                judged.Add((entry, release, score, decisions));
            }

            // One grab per version and pass: the best of what the feed brought.
            var winners = new Dictionary<int, (RssReleaseResponse Entry, ReleaseInfo Release)>();
            foreach (var group in judged.Where(j => j.Entry.AttributedVersionId is not null && j.Entry.Movie is not null).GroupBy(j => j.Entry.AttributedVersionId!.Value))
            {
                var best = ReleaseRanking.Order(group, j => j.Entry.QualityId, j => j.Score, j => j.Entry.RevisionVersion, j => j.Release.IndexerPriority, j => j.Release.Seeders ?? 0).First();
                winners[group.Key] = (best.Entry, best.Release);
            }

            var outcomes = new Dictionary<string, (int? GrabId, int? PickId, string? Error)>(StringComparer.Ordinal);
            foreach (var (versionId, (entry, release)) in winners)
            {
                var grab = await grabs.GrabAsync(GrabRequest.From(release, versionId), GrabTrigger, ct);
                if (grab.IsSuccess && grab.Value!.ShadowPickId is { } pickId)
                {
                    picked++;
                    outcomes[Key(entry)] = (null, pickId, null);
                }
                else if (grab.IsSuccess)
                {
                    grabbed++;
                    outcomes[Key(entry)] = (grab.Value!.GrabId, null, null);
                }
                else
                {
                    var failure = grab.Failure!;
                    logger.LogWarning("RSS: {Title} not grabbed for version {Version}: {Title2} {Detail}", entry.Title, versionId, failure.Title, failure.Detail);
                    outcomes[Key(entry)] = (null, null, failure.Detail is null ? failure.Title : $"{failure.Title}: {failure.Detail}");
                }
            }

            // A release only its seeders kept out, for a version this pass took nothing for: worth a quick look again.
            waiting.AddRange(judged
                .Where(j => j.Decisions.Any(d => MinimumSeedersSpecification.WaitsOnlyForSeeders(d) && !winners.ContainsKey(((MovieVersion)d.Target.Target).Id)))
                .Select(j => (j.Entry.IndexerId, false)));

            foreach (var (entry, _, _, _) in judged)
            {
                var stored = entry;
                if (entry.AttributedVersionId is not null)
                {
                    stored = outcomes.TryGetValue(Key(entry), out var outcome)
                        ? entry with { GrabId = outcome.GrabId, ShadowPickId = outcome.PickId, GrabError = outcome.Error }
                        : entry with { GrabError = $"A better release of this pass was {(picked > 0 ? "picked" : "grabbed")} for {entry.AttributedVersionName}" };
                }

                state.Upsert(stored);
            }

            if (sightings.Enabled)
            {
                var tmdbIds = movies.ToDictionary(m => m.Id, m => m.TmdbId);
                await sightings.RecordAsync(judged.Select(j => (j.Entry, j.Release, j.Entry.Movie is { } movie ? tmdbIds.GetValueOrDefault(movie.Id) : (int?)null, (int?)null)), now, ct);
            }
        }

        if (seriesFeed.Count > 0)
        {
            var pass = await seriesRss.RunAsync(seriesFeed, now, matchedAsFilm, ct);
            matched += pass.Matched;
            wouldGrab += pass.Attributed;
            grabbed += pass.Grabbed;
            picked += pass.Picked;
            waiting.AddRange(pass.WaitingForSeeders.Select(id => (id, true)));
        }

        var errors = results.Concat(seriesResults).Where(r => r.Error is not null)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        return (new RssSyncResponse(indexers.Count, results.Sum(r => r.Releases.Count) + seriesResults.Sum(r => r.Releases.Count), fresh, matched, wouldGrab, errors, grabbed, picked), waiting);
    }

    private static string Key(RssReleaseResponse entry) => $"{entry.IndexerId}:{entry.Guid}";

    private (RssReleaseResponse Entry, int Score, List<ReleaseDecision> Decisions) Judge(Indexer indexer, ReleaseInfo release, LibraryIndex library, Dictionary<int, List<TargetContext>> targets, CustomFormatCalculator calculator, DateTime now)
    {
        var parsed = ReleaseMatcher.TryParse(release.Title);
        var (movie, reason) = ReleaseMatcher.FindMovie(parsed, release, library);
        var formats = parsed is null ? [] : calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie?.Year, movie?.OriginalLanguage));

        var versions = new List<VersionDecisionResponse>();
        var decisions = new List<ReleaseDecision>();
        ReleaseDecision? attributed = null;
        if (movie is not null && parsed is not null)
        {
            var candidate = new ReleaseCandidate(release, parsed, indexer.MinimumSeeders) { CustomFormatIds = formats.Select(f => f.Id).ToList() };
            decisions = targets[movie.Id].Select(context => _evaluator.Evaluate(candidate, context)).ToList();
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
            attributedVersion?.Name), attributed?.CustomFormatScore ?? 0, decisions);
    }
}

/// <summary>The RSS pass as a scheduled task: interval and grace period from <see cref="RssSyncOptions"/>.</summary>
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
            : $"{pass.Fetched} release(s) from {pass.Indexers} indexer(s), {pass.New} new, {pass.Matched} matched, {pass.Grabbed} grabbed"
              + (pass.Picked > 0 ? $", {pass.Picked} picked in shadow mode" : string.Empty)
              + (pass.IndexerErrors.Count > 0 ? $", {pass.IndexerErrors.Count} indexer error(s)" : string.Empty);
    }
}

/// <summary>What a quick recheck did: why it ran, which indexers it read, and the pass over their feeds.</summary>
public sealed record RssRecheckOutcome(int Waiting, IReadOnlyList<string> Indexers, RssSyncResponse Pass);

/// <summary>
/// The RSS quick recheck: a one-off task a pass plans a few minutes ahead when a release was turned down only for its
/// seeders. It shows in System → Tasks beside the pass, shares its lock, and never plans another recheck.
/// </summary>
public sealed class RssRecheckJob(RssSyncService rss, IOptions<RssSyncOptions> options) : IScheduledJob
{
    public const string JobName = "rssRecheck";

    public string Name => JobName;

    public string DisplayName => "RSS quick recheck";

    public TimeSpan Interval => options.Value.QuickRecheck ?? TimeSpan.Zero;

    public bool Enabled => options.Value.QuickRecheck is not null;

    public TimeSpan InitialDelay => TimeSpan.Zero;

    public bool OneShot => true;

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var outcome = await rss.RecheckAsync(ct);
        if (outcome is null)
        {
            return "Nothing to recheck";
        }

        var pass = outcome.Pass;
        return $"{outcome.Waiting} release(s) waiting for seeders; {pass.Fetched} release(s) read from {string.Join(", ", outcome.Indexers)}, {pass.Matched} matched, {pass.Grabbed} grabbed"
               + (pass.Picked > 0 ? $", {pass.Picked} picked in shadow mode" : string.Empty)
               + (pass.IndexerErrors.Count > 0 ? $", {pass.IndexerErrors.Count} indexer error(s)" : string.Empty);
    }
}
