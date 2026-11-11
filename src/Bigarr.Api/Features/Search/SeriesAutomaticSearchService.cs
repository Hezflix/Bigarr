using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <param name="Picked">Releases recorded as shadow picks (shadow mode).</param>
/// <param name="Covered">Wanted episodes a picked or grabbed release covers.</param>
/// <param name="FallbackSeasons">Seasons the whole-series request left uncovered, searched one by one afterwards.</param>
public sealed record SeriesAutomaticSearchResult(
    int SeriesId,
    string SeriesTitle,
    int VersionId,
    string VersionName,
    int Wanted,
    int Covered,
    int Picked,
    int Grabbed,
    int Releases,
    List<int> FallbackSeasons,
    string? Detail);

/// <summary>
/// The whole-series rule (decision 7, 2026-11-10) shared by the automatic and the interactive search: one request by the
/// show's ids first (an ended show often has a complete-series pack), then, for every season still not covered by an
/// acceptable release, Sonarr's season-by-season search.
/// </summary>
public static class WholeSeriesSearch
{
    /// <summary>
    /// The seasons to search one by one after the whole-series request: those holding an episode of <paramref name="wanted"/>
    /// that no acceptable release covers, in order.
    /// </summary>
    public static List<int> UncoveredSeasons(IEnumerable<Episode> wanted, IReadOnlySet<int> coveredEpisodeIds)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(coveredEpisodeIds);

        return wanted.Where(e => !coveredEpisodeIds.Contains(e.Id)).Select(e => e.SeasonNumber).Distinct().Order().ToList();
    }

    /// <summary>Every indexer, every criterion, the releases deduplicated per indexer and guid.</summary>
    public static async Task<(List<ReleaseInfo> Releases, List<IndexerQueryResult> Results)> QueryAsync(
        IReadOnlyList<Indexer> indexers,
        IReadOnlyList<SeriesSearchCriteria> criteria,
        IndexerGateway gateway,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        var results = await IndexerQueries.RunAsync(indexers, async (indexer, token) =>
        {
            var found = new Dictionary<string, ReleaseInfo>(StringComparer.Ordinal);
            foreach (var one in criteria)
            {
                foreach (var release in await gateway.SearchSeriesAsync(indexer, one, token))
                {
                    found.TryAdd(release.Guid, release);
                }
            }

            return (IReadOnlyList<ReleaseInfo>)found.Values.ToList();
        }, ct);
        return (results.SelectMany(r => r.Releases).ToList(), results.ToList());
    }
}

/// <summary>
/// Automatic search for series versions (Sonarr's series search, the one it runs when a show is added with "search for
/// missing episodes"): the monitored, aired episodes without a file are wanted; the show is asked for by its ids first,
/// then season by season for whatever that left uncovered (<see cref="WholeSeriesSearch"/>). Each round judges every
/// release with the automatic rules for the version and takes the best ones without overlapping episodes (the RSS order:
/// quality, custom format score, revision, packs first, fewer episodes, indexer priority, seeders), through
/// <see cref="GrabService"/>: in shadow mode they become picks. Used by the follow's search on add (S4).
/// </summary>
public sealed class SeriesAutomaticSearchService(
    BigarrDbContext db,
    SeriesTargetsFactory targetsFactory,
    CustomFormatScorer scorer,
    IndexerGateway gateway,
    Indexers.IndexerService indexerService,
    GrabService grabs,
    TimeProvider time,
    ILogger<SeriesAutomaticSearchService> logger)
{
    public async Task<List<SeriesAutomaticSearchResult>> SearchAsync(IReadOnlyCollection<int> versionIds, SearchTrigger trigger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(versionIds);

        var results = new List<SeriesAutomaticSearchResult>();
        var versions = await db.SeriesVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).Select(v => new { v.Id, v.SeriesId }).ToListAsync(ct);
        if (versions.Count == 0)
        {
            return results;
        }

        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableAutomaticSearch).OrderBy(i => i.Priority).ToListAsync(ct);
        var evaluator = trigger == SearchTrigger.User ? ReleaseEvaluator.Interactive() : ReleaseEvaluator.Automatic();
        var calculator = await scorer.CalculatorAsync(ct);
        var triggerName = AutomaticSearchService.TriggerName(trigger);
        foreach (var version in versions.OrderBy(v => v.Id))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await SearchOneAsync(version.SeriesId, version.Id, indexers, evaluator, calculator, triggerName, ct));
        }

        return results;
    }

    private async Task<SeriesAutomaticSearchResult> SearchOneAsync(int seriesId, int versionId, List<Indexer> indexers, ReleaseEvaluator evaluator, CustomFormatCalculator calculator, string trigger, CancellationToken ct)
    {
        var targets = await targetsFactory.LoadAsync(seriesId, ct);
        var version = targets?.Series.Versions.FirstOrDefault(v => v.Id == versionId);
        if (targets is null || version is null)
        {
            return new SeriesAutomaticSearchResult(seriesId, string.Empty, versionId, string.Empty, 0, 0, 0, 0, 0, [], "Version not found.");
        }

        var series = targets.Series;
        var now = time.GetUtcNow().UtcDateTime;
        var wanted = targets.Episodes
            .Where(e => e.HasAired(now) && targets.Slot(versionId, e.Id) is { } slot && slot.Target.Monitored && slot.File is null)
            .OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .ToList();
        SeriesAutomaticSearchResult Done(int covered, int picked, int grabbed, int releases, List<int> fallback, string? detail) =>
            new(series.Id, series.Title, versionId, version.Name, wanted.Count, covered, picked, grabbed, releases, fallback, detail);

        if (!version.Monitored || wanted.Count == 0)
        {
            return Done(0, 0, 0, 0, [], "No monitored, aired episode without a file.");
        }

        if (indexers.Count == 0)
        {
            return Done(0, 0, 0, 0, [], "No indexer has automatic search enabled.");
        }

        var minimumSeeders = indexers.ToDictionary(i => i.Id, i => i.MinimumSeeders);
        var index = new SeriesTitleIndex([targets.Indexed], targets.SceneNames);
        var wantedIds = wanted.Select(e => e.Id).ToHashSet();
        var taken = new HashSet<int>();
        var picked = 0;
        var grabbed = 0;
        var releasesSeen = 0;
        string? stop = null;

        var held = 0;
        async Task RoundAsync(List<SeriesSearchCriteria> criteria, EpisodeSearchScope scope, bool orderFallback = false)
        {
            var (releases, results) = await WholeSeriesSearch.QueryAsync(indexers, criteria, gateway, ct);
            await indexerService.FillSeriesCategoriesAsync(results.Where(r => r.Error is null).Select(r => r.Indexer), ct);
            releasesSeen += releases.Count;
            var approved = new List<(ReleaseInfo Release, SeriesJudgement Judgement, ReleaseDecision Decision)>();
            foreach (var release in releases)
            {
                var parsed = SeriesReleaseJudge.TryParse(release.Title);
                if (parsed is null)
                {
                    continue;
                }

                var match = index.Find(parsed, release.TvdbId is { } t && t == series.TvdbId ? t : null);
                if (match.Series is null || (release.TvdbId is { } tvdb && series.TvdbId is { } own && tvdb != own))
                {
                    continue;
                }

                var judgement = SeriesReleaseJudge.Judge(release, parsed, targets, match.SceneSeasonNumber, calculator, evaluator, minimumSeeders.GetValueOrDefault(release.IndexerId), scope, versionId, orderFallback: orderFallback);
                if (judgement.HeldReason is not null)
                {
                    held++;
                    logger.LogInformation("Series search ({Trigger}) for {Series} [{Version}]: {Title} held for a manual choice (matched on the series' order only)", trigger, series.Title, version.Name, release.Title);
                }

                if (judgement.IsMapped && judgement.Decisions.FirstOrDefault() is { Approved: true } decision && judgement.Episodes.Any(e => wantedIds.Contains(e.Id)))
                {
                    approved.Add((release, judgement, decision));
                }
            }

            var ranked = approved
                .OrderByDescending(a => ReleaseRanking.WeightOf(a.Judgement.Parsed?.Quality?.Quality?.Id ?? 0))
                .ThenByDescending(a => a.Decision.CustomFormatScore)
                .ThenByDescending(a => a.Judgement.Parsed?.Quality?.Revision?.Version ?? 1)
                .ThenByDescending(a => a.Judgement.Parsed?.FullSeason ?? false)
                .ThenBy(a => series.SeriesType == SeriesType.Anime ? -a.Judgement.Episodes.Count : a.Judgement.Episodes.Count)
                .ThenBy(a => a.Release.IndexerPriority)
                .ThenByDescending(a => a.Release.Seeders ?? 0)
                .ToList();
            foreach (var (release, judgement, _) in ranked)
            {
                var episodeIds = judgement.Episodes.Select(e => e.Id).ToList();
                if (stop is not null || episodeIds.Any(taken.Contains))
                {
                    continue;
                }

                var grab = await grabs.GrabAsync(GrabRequest.ForEpisodes(release, versionId, episodeIds, judgement.Numbering), trigger, ct);
                if (!grab.IsSuccess)
                {
                    var failure = grab.Failure!;
                    stop = failure.Detail is null ? failure.Title : $"{failure.Title}: {failure.Detail}";
                    logger.LogInformation("Series search ({Trigger}) for {Series} [{Version}]: {Title} not taken: {Reason}", trigger, series.Title, version.Name, release.Title, stop);
                    continue;
                }

                taken.UnionWith(episodeIds);
                if (grab.Value!.Shadow)
                {
                    picked++;
                }
                else
                {
                    grabbed++;
                }
            }
        }

        // 1. One request by the show's ids: a complete-series pack, or whatever the indexers return for the show.
        await RoundAsync(SeriesSearchCriteriaBuilder.ForSeries(series, targets.SceneNames), new EpisodeSearchScope(EpisodeSearchKind.Series, wantedIds, null, series.Title));

        // 2. Season by season for the seasons that request left with wanted episodes uncovered, like Sonarr.
        var fallback = stop is null ? WholeSeriesSearch.UncoveredSeasons(wanted, taken) : [];
        foreach (var season in fallback)
        {
            var seasonEpisodes = targets.Episodes.Where(e => e.SeasonNumber == season).ToList();
            var requested = seasonEpisodes.Select(e => e.Id).ToHashSet();
            await RoundAsync(
                SeriesSearchCriteriaBuilder.ForSeason(series, season, seasonEpisodes, targets.SceneNames),
                new EpisodeSearchScope(EpisodeSearchKind.Season, requested, season, season == 0 ? "Specials" : $"Season {season}"));
            if (stop is not null)
            {
                break;
            }
        }

        // 3. Blueprint §8: still uncovered and the series shows another order: its seasons asked by the order's numbers,
        // the releases mapped on TVDB first, then on the order (a hit there confirmed by the release, else held).
        var uncovered = wanted.Where(e => !taken.Contains(e.Id)).ToList();
        if (stop is null && uncovered.Count > 0 && series.Ordering != EpisodeOrder.Tvdb)
        {
            var orderCriteria = SeriesSearchCriteriaBuilder.ForOrderSeasons(series, uncovered, targets.SceneNames);
            if (orderCriteria.Count > 0)
            {
                await RoundAsync(orderCriteria, new EpisodeSearchScope(EpisodeSearchKind.Season, uncovered.Select(e => e.Id).ToHashSet(), null, "order"), orderFallback: true);
            }
        }

        var covered = wanted.Count(e => taken.Contains(e.Id));
        var searchedTargets = wanted.Select(e => targets.Slot(versionId, e.Id)!.Target.Id).ToList();
        await db.AcquisitionTargets.Where(t => searchedTargets.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastSearchAt, now), ct);

        logger.LogInformation(
            "Series search ({Trigger}) for {Series} [{Version}]: {Releases} release(s), {Covered}/{Wanted} wanted episode(s) covered, {Picked} pick(s), {Grabbed} grab(s){Fallback}",
            trigger, series.Title, version.Name, releasesSeen, covered, wanted.Count, picked, grabbed, fallback.Count == 0 ? string.Empty : $", seasons searched one by one: {string.Join(", ", fallback)}");
        return Done(covered, picked, grabbed, releasesSeen, fallback, stop ?? (held > 0 ? $"{held} release(s) matched on the series' order only, held for a manual choice (interactive search)." : null));
    }
}
