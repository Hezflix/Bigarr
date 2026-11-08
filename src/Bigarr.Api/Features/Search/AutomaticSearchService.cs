using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Parsing.Model;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Automatic search (blueprint §5, mode auto): every indexer with automatic search enabled, each release judged for
/// the versions asked, the best approved one grabbed. Triggered on add, after a failed download, by the cutoff-unmet
/// job and by the user; only the user's searches ignore monitoring and availability.
/// </summary>
public sealed class AutomaticSearchService(
    BigarrDbContext db,
    TargetContextFactory contexts,
    CustomFormatScorer scorer,
    IndexerGateway gateway,
    GrabService grabs,
    TimeProvider time,
    ILogger<AutomaticSearchService> logger)
{
    /// <summary>Releases tried in turn when a client refuses one (a dead link is no reason to give up the search).</summary>
    public const int MaxGrabAttempts = 3;

    public async Task<List<AutomaticSearchResult>> SearchAsync(IReadOnlyCollection<int> versionIds, SearchTrigger trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(versionIds);

        var ids = versionIds.Distinct().ToList();
        var results = new List<AutomaticSearchResult>();
        if (ids.Count == 0)
        {
            return results;
        }

        var movies = await contexts.MoviesWithVersions().Where(m => m.Versions.Any(v => ids.Contains(v.Id))).ToListAsync(ct);
        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableAutomaticSearch).OrderBy(i => i.Priority).ToListAsync(ct);
        var evaluator = trigger == SearchTrigger.User ? ReleaseEvaluator.Interactive() : ReleaseEvaluator.Automatic();
        var calculator = await scorer.CalculatorAsync(ct);
        var minimumSeeders = indexers.ToDictionary(i => i.Id, i => i.MinimumSeeders);
        var triggerName = TriggerName(trigger);

        foreach (var movie in movies)
        {
            ct.ThrowIfCancellationRequested();
            var wanted = movie.Versions.Where(v => ids.Contains(v.Id)).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToList();
            if (indexers.Count == 0)
            {
                results.AddRange(wanted.Select(v => new AutomaticSearchResult(movie.Id, movie.Title, v.Id, v.Name, AutomaticSearchOutcome.NoIndexer, null, null, "No indexer has automatic search enabled.", 0, 0, [])));
                continue;
            }

            var criteria = InteractiveSearchService.CriteriaFor(movie);
            var queried = await IndexerQueries.RunAsync(indexers, (indexer, token) => gateway.SearchAsync(indexer, criteria, token), ct);
            var errors = queried.Where(r => r.Error is not null).Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message)).ToList();
            var releases = queried.SelectMany(r => r.Releases).ToList();
            logger.LogInformation("Automatic search ({Trigger}) for {Movie}: {Releases} release(s) from {Indexers} indexer(s)", triggerName, movie.Title, releases.Count, indexers.Count);

            // Parse and match once per release; the verdict is per version.
            var parsedReleases = releases
                .Select(release =>
                {
                    var parsed = ReleaseMatcher.TryParse(release.Title);
                    var mismatch = ReleaseMatcher.Match(parsed, release, movie);
                    var formats = parsed is null || mismatch is not null ? [] : calculator.Match(CustomFormatScorer.ReleaseInput(parsed, release, movie.Year, movie.OriginalLanguage)).Select(f => f.Id).ToList();
                    return (Release: release, Parsed: parsed, Mismatch: mismatch, Formats: formats);
                })
                .Where(r => r.Parsed is not null && r.Mismatch is null)
                .ToList();

            var targets = await contexts.BuildForGrabAsync(movie, wanted, ct);
            foreach (var context in targets)
            {
                var version = (MovieVersion)context.Target;
                if (context.HasActiveGrab)
                {
                    results.Add(new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.Skipped, null, null, $"Version {version.Name} already has an active download.", releases.Count, 0, errors));
                    continue;
                }

                var approved = new List<(ReleaseInfo Release, ParsedMovieInfo Parsed, ReleaseDecision Decision)>();
                var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var (release, parsed, _, formats) in parsedReleases)
                {
                    var candidate = new ReleaseCandidate(release, parsed!, minimumSeeders.GetValueOrDefault(release.IndexerId)) { CustomFormatIds = formats };
                    var decision = evaluator.Evaluate(candidate, context);
                    if (decision.Approved)
                    {
                        approved.Add((release, parsed!, decision));
                    }
                    else
                    {
                        var reason = decision.Rejections[0].Reason;
                        rejections[reason] = rejections.GetValueOrDefault(reason) + 1;
                    }
                }

                var ranked = ReleaseRanking.Order(
                    approved,
                    a => a.Parsed.Quality?.Quality?.Id ?? 0,
                    a => a.Decision.CustomFormatScore,
                    a => a.Parsed.Quality?.Revision?.Version ?? 1,
                    a => a.Release.IndexerPriority,
                    a => a.Release.Seeders ?? 0).ToList();

                var result = await GrabBestAsync(movie, version, ranked, rejections, releases.Count, errors, triggerName, ct);
                results.Add(result);
                // A shadow pick leaves the version wanted: it backs off like an empty search rather than search every hour.
                await RecordSearchAsync(version.Id, result.Outcome == AutomaticSearchOutcome.Grabbed, ct);
            }
        }

        foreach (var missing in ids.Where(id => results.All(r => r.VersionId != id)))
        {
            results.Add(new AutomaticSearchResult(0, string.Empty, missing, string.Empty, AutomaticSearchOutcome.Skipped, null, null, "Version not found.", 0, 0, []));
        }

        return results;
    }

    private async Task<AutomaticSearchResult> GrabBestAsync(
        Movie movie,
        MovieVersion version,
        List<(ReleaseInfo Release, ParsedMovieInfo Parsed, ReleaseDecision Decision)> ranked,
        Dictionary<string, int> rejections,
        int releases,
        List<IndexerErrorResponse> errors,
        string trigger,
        CancellationToken ct)
    {
        if (ranked.Count == 0)
        {
            var detail = releases == 0
                ? errors.Count > 0 ? "No release came back; " + string.Join(" · ", errors.Select(e => $"{e.Indexer}: {e.Message}")) : "No release came back."
                : Summarize(rejections);
            return new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.NothingFound, null, null, detail, releases, 0, errors);
        }

        string? lastError = null;
        foreach (var (release, _, _) in ranked.Take(MaxGrabAttempts))
        {
            var grab = await grabs.GrabAsync(GrabRequest.From(release, version.Id), trigger, ct);
            if (grab.IsSuccess && grab.Value!.Shadow)
            {
                return new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.Picked, release.Title, null, "Shadow mode: recorded as Bigarr's pick, nothing was sent to a download client.", releases, ranked.Count, errors);
            }

            if (grab.IsSuccess)
            {
                return new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.Grabbed, release.Title, grab.Value!.GrabId, null, releases, ranked.Count, errors);
            }

            var failure = grab.Failure!;
            if (failure.StatusCode == StatusCodes.Status409Conflict)
            {
                return new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.Skipped, release.Title, null, failure.Detail ?? failure.Title, releases, ranked.Count, errors);
            }

            lastError = failure.Detail is null ? failure.Title : $"{failure.Title}: {failure.Detail}";
            if (failure.Title != "Release cannot be downloaded")
            {
                // No client, or none reachable: the next release would fare no better.
                break;
            }

            logger.LogInformation("{Title} refused by the download client, trying the next release for {Movie} [{Version}]", release.Title, movie.Title, version.Name);
        }

        return new AutomaticSearchResult(movie.Id, movie.Title, version.Id, version.Name, AutomaticSearchOutcome.Failed, ranked[0].Release.Title, null, lastError, releases, ranked.Count, errors);
    }

    /// <summary>Search bookkeeping for the backoff: a grab resets the counter, an empty search bumps it.</summary>
    private async Task RecordSearchAsync(int versionId, bool grabbed, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.AcquisitionTargets.Where(t => t.Id == versionId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.LastSearchAt, now)
            .SetProperty(t => t.SearchAttempts, t => grabbed ? 0 : t.SearchAttempts + 1), ct);
    }

    private static string Summarize(Dictionary<string, int> rejections)
    {
        if (rejections.Count == 0)
        {
            return "No release matched the film.";
        }

        var top = rejections.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal).Take(3).Select(r => $"{r.Key} ({r.Value})");
        return string.Join(" · ", top);
    }

    public static string TriggerName(SearchTrigger trigger) => trigger switch
    {
        SearchTrigger.User => "search:user",
        SearchTrigger.Added => "search:added",
        SearchTrigger.FailedDownload => "search:failed",
        SearchTrigger.CutoffUnmet => "search:cutoffUnmet",
        _ => "search",
    };
}
