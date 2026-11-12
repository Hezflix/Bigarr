using System.Diagnostics;
using Bigarr.Api.Infrastructure;
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
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Interactive search for a series (Sonarr's manual search): an episode, a season (packs and single
/// episodes together) or the whole series, every indexer in parallel, each release parsed with Sonarr's parser, mapped
/// to episodes and judged by every version of the series, rejections spelled out. As for films the search keeps no
/// state; the grab sends the release back with the episodes it covers.
/// </summary>
public sealed class SeriesSearchService(BigarrDbContext db, SeriesTargetsFactory targetsFactory, Indexers.IndexerService indexerService, CustomFormatScorer scorer, IndexerGateway gateway, TimeProvider time, ILogger<SeriesSearchService> logger)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Interactive();

    /// <param name="orderSeason"><paramref name="seasonNumber"/> is a season of the series' alternative order (its page shows that order).</param>
    public async Task<Result<SeriesSearchResponse>> SearchAsync(int seriesId, int? episodeId, int? seasonNumber, int? versionId, string? query, CancellationToken ct, bool orderSeason = false)
    {
        var targets = await targetsFactory.LoadAsync(seriesId, ct);
        if (targets is null)
        {
            return Failure.NotFound("Series");
        }

        var series = targets.Series;
        if (versionId is not null && series.Versions.All(v => v.Id != versionId))
        {
            return Failure.NotFound("Version");
        }

        string scope;
        string label;
        HashSet<int> requested;
        List<SeriesSearchCriteria> criteria;
        if (episodeId is not null)
        {
            var episode = targets.Episodes.FirstOrDefault(e => e.Id == episodeId);
            if (episode is null)
            {
                return Failure.NotFound("Episode");
            }

            scope = "episode";
            label = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
            requested = [episode.Id];
            criteria = SeriesSearchCriteriaBuilder.ForEpisode(series, episode, targets.SceneNames);
        }
        else if (seasonNumber is not null && orderSeason && series.Ordering != EpisodeOrder.Tvdb)
        {
            // A season of the order on screen: its episodes, asked first by the TVDB seasons they fall in.
            var seasonEpisodes = targets.Episodes.Where(e => e.InOrder && e.OrderSeasonNumber == seasonNumber).ToList();
            if (seasonEpisodes.Count == 0)
            {
                return Failure.NotFound("Season");
            }

            scope = "season";
            label = seasonNumber == 0 ? "Specials" : $"Season {seasonNumber}";
            requested = seasonEpisodes.Select(e => e.Id).ToHashSet();
            criteria = seasonEpisodes.GroupBy(e => e.SeasonNumber).OrderBy(g => g.Key)
                .SelectMany(g => SeriesSearchCriteriaBuilder.ForSeason(series, g.Key, targets.Episodes.Where(e => e.SeasonNumber == g.Key).ToList(), targets.SceneNames))
                .ToList();
        }
        else if (seasonNumber is not null)
        {
            var seasonEpisodes = targets.Episodes.Where(e => e.SeasonNumber == seasonNumber).ToList();
            if (seasonEpisodes.Count == 0)
            {
                return Failure.NotFound("Season");
            }

            scope = "season";
            label = seasonNumber == 0 ? "Specials" : $"Season {seasonNumber}";
            requested = seasonEpisodes.Select(e => e.Id).ToHashSet();
            criteria = SeriesSearchCriteriaBuilder.ForSeason(series, seasonNumber.Value, seasonEpisodes, targets.SceneNames);
        }
        else
        {
            scope = "series";
            label = series.Title;
            requested = targets.Episodes.Select(e => e.Id).ToHashSet();
            criteria = SeriesSearchCriteriaBuilder.ForSeries(series, targets.SceneNames);
        }

        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (query is not null)
        {
            criteria = SeriesSearchCriteriaBuilder.ForText(series, query);
        }

        var indexers = await db.Indexers.AsNoTracking().Where(i => i.EnableInteractiveSearch).OrderBy(i => i.Priority).ToListAsync(ct);
        if (indexers.Count == 0)
        {
            return Failure.Invalid("No indexer available", "No indexer has interactive search enabled. Add one in Settings → Indexers.");
        }

        var stopwatch = Stopwatch.StartNew();
        var (found, results) = await WholeSeriesSearch.QueryAsync(indexers, criteria, gateway, ct);
        var calculator = await scorer.CalculatorAsync(ct);
        var minimumSeeders = indexers.ToDictionary(i => i.Id, i => i.MinimumSeeders);
        var index = new SeriesTitleIndex([targets.Indexed], targets.SceneNames);
        var searchScope = new EpisodeSearchScope(scope switch { "episode" => EpisodeSearchKind.Episode, "season" => EpisodeSearchKind.Season, _ => EpisodeSearchKind.Series }, requested, seasonNumber, label);
        var releases = found.Select(release => Evaluate(release, targets, index, searchScope, versionId, minimumSeeders.GetValueOrDefault(release.IndexerId), calculator)).ToList();

        // Decision 7: a whole-series search starts with one request by the show's ids, then searches season by season
        // the seasons no acceptable release of that request covers (Sonarr's own series search goes season by season).
        var fallbackSeasons = new List<int>();
        if (scope == "series" && query is null)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var covered = releases
                .Where(r => r.Response.MatchesSeries && r.Response.Versions.Any(v => v.Approved))
                .SelectMany(r => r.Response.Episodes.Select(e => e.Id))
                .ToHashSet();
            fallbackSeasons = WholeSeriesSearch.UncoveredSeasons(targets.Episodes.Where(e => e.SeasonNumber > 0 && e.HasAired(now)), covered);
            var seen = found.Select(r => (r.IndexerId, r.Guid)).ToHashSet();
            foreach (var season in fallbackSeasons)
            {
                var seasonEpisodes = targets.Episodes.Where(e => e.SeasonNumber == season).ToList();
                var (more, moreResults) = await WholeSeriesSearch.QueryAsync(indexers, SeriesSearchCriteriaBuilder.ForSeason(series, season, seasonEpisodes, targets.SceneNames), gateway, ct);
                results.AddRange(moreResults.Where(r => r.Error is not null && results.All(e => e.Indexer.Id != r.Indexer.Id || e.Error is null)));
                releases.AddRange(more.Where(r => seen.Add((r.IndexerId, r.Guid))).Select(release => Evaluate(release, targets, index, searchScope, versionId, minimumSeeders.GetValueOrDefault(release.IndexerId), calculator)));
            }
        }

        // TVDB numbering found no acceptable release for what was asked: the series' alternative order is
        // tried, on the releases already found and with a request by the order's numbers. A hit there is confirmed by
        // the release (air date, episode title) or held for a manual choice.
        var orderFallback = false;
        if (series.Ordering != EpisodeOrder.Tvdb && query is null && scope != "series"
            && !releases.Any(r => r.Response.MatchesSeries && r.Response.Versions.Any(v => v.Approved) && r.Response.Episodes.Any(e => requested.Contains(e.Id))))
        {
            orderFallback = true;
            var asked = targets.Episodes.Where(e => requested.Contains(e.Id)).ToList();
            var orderCriteria = scope == "episode"
                ? SeriesSearchCriteriaBuilder.ForOrderEpisode(series, asked[0], targets.SceneNames)
                : SeriesSearchCriteriaBuilder.ForOrderSeasons(series, asked, targets.SceneNames);
            var all = new List<ReleaseInfo>(found);
            if (orderCriteria.Count > 0)
            {
                var seen = found.Select(r => (r.IndexerId, r.Guid)).ToHashSet();
                var (more, moreResults) = await WholeSeriesSearch.QueryAsync(indexers, orderCriteria, gateway, ct);
                results.AddRange(moreResults.Where(r => r.Error is not null && results.All(e => e.Indexer.Id != r.Indexer.Id || e.Error is null)));
                all.AddRange(more.Where(r => seen.Add((r.IndexerId, r.Guid))));
            }

            releases = all.Select(release => Evaluate(release, targets, index, searchScope, versionId, minimumSeeders.GetValueOrDefault(release.IndexerId), calculator, orderFallback: true)).ToList();
        }

        logger.LogInformation("Interactive search for {Series} {Label}{Query}: {Releases} release(s) from {Indexers} indexer(s) in {Elapsed} ms{Fallback}{Order}",
            series.Title, label, query is null ? string.Empty : $" by text '{query}'", releases.Count, indexers.Count, stopwatch.ElapsedMilliseconds,
            fallbackSeasons.Count == 0 ? string.Empty : $", then season by season: {string.Join(", ", fallbackSeasons)}",
            orderFallback ? ", then on the series' order" : string.Empty);

        await indexerService.FillSeriesCategoriesAsync(results.Where(r => r.Error is null).Select(r => r.Indexer).DistinctBy(i => i.Id), ct);

        var ordered = releases
            .OrderByDescending(r => r.Response.MatchesSeries && r.Response.Episodes.Count > 0)
            .ThenByDescending(r => r.Response.Versions.Any(v => v.Approved))
            .ThenByDescending(r => ReleaseRanking.WeightOf(r.Response.QualityId))
            .ThenByDescending(r => r.Response.Versions.Count == 0 ? 0 : r.Response.Versions.Max(v => v.CustomFormatScore))
            .ThenByDescending(r => r.Response.RevisionVersion)
            .ThenByDescending(r => r.Response.FullSeason)
            .ThenBy(r => series.SeriesType == SeriesType.Anime ? -r.Response.Episodes.Count : r.Response.Episodes.Count)
            .ThenBy(r => r.Response.IndexerPriority)
            .ThenByDescending(r => r.Response.Seeders ?? 0)
            .Select(r => r.Response)
            .ToList();

        var errors = results.Where(r => r.Error is not null)
            .DistinctBy(r => r.Indexer.Id)
            .Select(r => new IndexerErrorResponse(r.Indexer.Id, r.Indexer.Name, r.Error!.Kind, r.Error.Message))
            .ToList();

        return Result<SeriesSearchResponse>.Ok(new SeriesSearchResponse(series.Id, scope, seasonNumber, episodeId, label, time.GetUtcNow().UtcDateTime, indexers.Count, ordered, errors, query, fallbackSeasons, orderFallback));
    }

    private static (SeriesReleaseResponse Response, int Order) Evaluate(
        ReleaseInfo release,
        SeriesTargets targets,
        SeriesTitleIndex index,
        EpisodeSearchScope scope,
        int? versionId,
        int minimumSeeders,
        CustomFormatCalculator calculator,
        bool orderFallback = false)
    {
        var series = targets.Series;
        var parsed = SeriesReleaseJudge.TryParse(release.Title);
        var rejections = new List<RejectionResponse>();

        int? sceneSeason = null;
        var matches = false;
        var special = false;

        // Sonarr: a name without an episode number (or with a possible special) may name a special by its title, when
        // the release is this show by TVDB id or by the start of its name.
        if ((parsed is null || parsed.IsPossibleSpecialEpisode)
            && ((release.TvdbId is { } releaseTvdb && releaseTvdb == series.TvdbId) || index.FindInexact(release.Title) is not null)
            && SeriesReleaseJudge.ParseSpecial(parsed, release.Title, targets) is { } byTitle)
        {
            parsed = byTitle;
            matches = true;
            special = true;
        }

        if (parsed is not null && !special)
        {
            var match = index.Find(parsed, release.TvdbId is { } t && t == series.TvdbId ? t : null);
            if (release.TvdbId is { } tvdb && series.TvdbId is { } own && tvdb != own)
            {
                rejections.Add(new RejectionResponse($"Release is TVDB {tvdb}, not {own}", false));
            }
            else if (match.Series is null)
            {
                rejections.Add(new RejectionResponse($"Title '{parsed.SeriesTitle}' does not match {series.Title}", false));
            }
            else
            {
                matches = true;
                sceneSeason = match.SceneSeasonNumber;
            }

            // Not the show, maybe: still mapped against it and judged, the user may know better and grab by hand.
            sceneSeason ??= index.SceneSeasonFor(parsed.SeriesTitle, series.TvdbId);
        }

        var judgement = SeriesReleaseJudge.Judge(release, parsed, targets, sceneSeason, calculator, ReleaseEvaluator.Interactive(), minimumSeeders, scope, versionId, sceneSource: !special, orderFallback: orderFallback && !special);
        if (judgement.UnmatchedReason is { } reason)
        {
            rejections.Add(new RejectionResponse(reason, false));
        }

        var versions = judgement.Decisions.Select(d =>
        {
            var coverage = d.Target.Episodes!;
            var version = targets.Series.Versions.First(v => v.Id == ((EpisodeTarget)d.Target.Target).VersionId);
            return new SeriesVersionDecisionResponse(
                version.Id,
                version.Name,
                d.Approved,
                d.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList(),
                d.CustomFormatScore,
                coverage.Slots.Select(s => s.Target.Id).ToList());
        }).ToList();
        var suggested = VersionAttribution.Suggest(judgement.Decisions) is { } best ? ((EpisodeTarget)best.Target.Target).VersionId : (int?)null;

        var quality = parsed?.Quality?.Quality ?? ParserQuality.Unknown;
        var revision = parsed?.Quality?.Revision;
        return (new SeriesReleaseResponse(
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
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            parsed?.Languages?.Where(l => l.Id != 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
            judgement.Formats.Select(f => f.Name).ToList(),
            string.IsNullOrWhiteSpace(parsed?.SeriesTitle) ? null : parsed!.SeriesTitle,
            matches,
            rejections,
            judgement.Episodes.Select(e => new SeriesEpisodeRef(e.Id, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.Title)).ToList(),
            judgement.Episodes.Count == 0 ? null : SeriesReleaseJudge.Label(judgement.Episodes),
            parsed?.FullSeason ?? false,
            versions,
            suggested,
            judgement.IsMapped ? judgement.Numbering : null,
            judgement.HeldReason is not null), 0);
    }
}
