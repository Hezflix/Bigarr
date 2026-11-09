using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

public sealed record SeriesRssOutcome(int Matched, int Attributed, int Grabbed, int Picked);

/// <summary>
/// The series half of the RSS pass: each release of the TV feed is parsed with Sonarr's parser, matched to a series of
/// the library (TVDB id from the indexer, XEM scene name, title, title + year: Sonarr's order), mapped to episodes and
/// judged by every version with the automatic rules, then attributed to one version. The best releases of the pass are
/// grabbed without overlap (Sonarr's prioritisation: quality, custom format score, revision, season packs first, then
/// fewer episodes, indexer priority, seeders), through <see cref="GrabService"/>: in shadow mode they become picks. A
/// real grab for a series is refused before reaching a client until series downloads can be imported.
/// </summary>
public sealed class SeriesRssPass(
    BigarrDbContext db,
    SeriesTargetsFactory targetsFactory,
    CustomFormatScorer scorer,
    GrabService grabs,
    ReleaseSightings sightings,
    RssFeedState state,
    Indexers.IndexerService indexerService,
    ILogger<SeriesRssPass> logger)
{
    private readonly ReleaseEvaluator _evaluator = ReleaseEvaluator.Automatic();

    public async Task<SeriesRssOutcome> RunAsync(IReadOnlyList<(Indexer Indexer, ReleaseInfo Release)> feed, DateTime now, CancellationToken ct)
    {
        await indexerService.FillSeriesCategoriesAsync(feed.Select(f => f.Indexer).DistinctBy(i => i.Id), ct);
        var index = await BuildIndexAsync(ct);

        // Find the show of every release first, then load only those shows.
        var located = feed
            .OrderBy(f => f.Release.PublishDate)
            .Select(f =>
            {
                var parsed = SeriesReleaseJudge.TryParse(f.Release.Title);
                if (parsed is null)
                {
                    return (f.Indexer, f.Release, Parsed: parsed, Match: SeriesMatch.NotFound, Reason: (string?)"Release title could not be parsed as an episode");
                }

                var match = index.Find(parsed, f.Release.TvdbId);
                string? reason = match switch
                {
                    { Series: not null } => null,
                    { IsAmbiguous: true } => $"Several series in the library match '{parsed.SeriesTitle}': {string.Join(", ", match.Ambiguous.Select(s => s.Title))}",
                    _ when f.Release.TvdbId is { } tvdb => $"TVDB {tvdb} is not in the library and no title matches '{parsed.SeriesTitle}'",
                    _ => $"No series in the library matches '{parsed.SeriesTitle}'",
                };
                return (f.Indexer, f.Release, Parsed: parsed, Match: match, Reason: reason);
            })
            .ToList();

        var seriesIds = located.Where(l => l.Match.Series is not null).Select(l => l.Match.Series!.Id).Distinct().ToList();
        var targets = await targetsFactory.LoadAsync(seriesIds, now, ct);
        var calculator = await scorer.CalculatorAsync(ct);

        var judged = new List<(RssReleaseResponse Entry, ReleaseInfo Release, SeriesJudgement? Judgement, SeriesTargets? Targets)>();
        foreach (var (indexer, release, parsed, match, reason) in located)
        {
            SeriesTargets? show = match.Series is { } found ? targets.GetValueOrDefault(found.Id) : null;
            SeriesJudgement? judgement = null;
            if (show is not null)
            {
                judgement = SeriesReleaseJudge.Judge(release, parsed, show, match.SceneSeasonNumber, calculator, _evaluator, indexer.MinimumSeeders);
            }

            judged.Add((Entry(release, parsed, show, judgement, reason ?? judgement?.UnmatchedReason, now), release, judgement, show));
        }

        // One grab per episode target and pass, best first, a release only when none of its targets is already taken.
        var taken = new HashSet<int>();
        var outcomes = new Dictionary<string, (int? PickId, string? Error)>(StringComparer.Ordinal);
        var grabbed = 0;
        var picked = 0;
        var ranked = judged
            .Where(j => j.Judgement?.Attributed is not null)
            .OrderByDescending(j => ReleaseRanking.WeightOf(j.Entry.QualityId))
            .ThenByDescending(j => j.Judgement!.Attributed!.CustomFormatScore)
            .ThenByDescending(j => j.Entry.RevisionVersion)
            .ThenByDescending(j => j.Judgement!.Parsed?.FullSeason ?? false)
            .ThenBy(j => j.Targets!.Series.SeriesType == SeriesType.Anime ? -j.Judgement!.Episodes.Count : j.Judgement!.Episodes.Count)
            .ThenBy(j => j.Release.IndexerPriority)
            .ThenByDescending(j => j.Release.Seeders ?? 0)
            .ToList();
        foreach (var (entry, release, judgement, _) in ranked)
        {
            var attributed = judgement!.Attributed!;
            var targetIds = attributed.Target.Episodes!.Slots.Select(s => s.Target.Id).ToList();
            if (targetIds.Any(taken.Contains))
            {
                continue;
            }

            taken.UnionWith(targetIds);
            var versionId = ((EpisodeTarget)attributed.Target.Target).VersionId;
            if (!sightings.Enabled)
            {
                outcomes[Key(entry)] = (null, GrabService.SeriesGrabGated);
                continue;
            }

            var grab = await grabs.GrabAsync(GrabRequest.ForEpisodes(release, versionId, judgement.Episodes.Select(e => e.Id)), RssSyncService.GrabTrigger, ct);
            if (grab.IsSuccess && grab.Value!.ShadowPickId is { } pickId)
            {
                picked++;
                outcomes[Key(entry)] = (pickId, null);
            }
            else if (grab.IsSuccess)
            {
                grabbed++;
                outcomes[Key(entry)] = (null, null);
            }
            else
            {
                var failure = grab.Failure!;
                logger.LogWarning("RSS: {Title} not picked for series version {Version}: {Failure} {Detail}", entry.Title, versionId, failure.Title, failure.Detail);
                outcomes[Key(entry)] = (null, failure.Detail is null ? failure.Title : $"{failure.Title}: {failure.Detail}");
            }
        }

        foreach (var (entry, _, _, _) in judged)
        {
            var stored = entry;
            if (entry.AttributedVersionId is not null)
            {
                stored = outcomes.TryGetValue(Key(entry), out var outcome)
                    ? entry with { ShadowPickId = outcome.PickId, GrabError = outcome.Error }
                    : entry with { GrabError = $"A better release of this pass covers these episodes for {entry.AttributedVersionName}" };
            }

            state.Upsert(stored);
        }

        if (sightings.Enabled)
        {
            await sightings.RecordAsync(judged.Select(j => (j.Entry, j.Release, (int?)null, j.Targets?.Series.TvdbId)), now, ct);
        }

        return new SeriesRssOutcome(
            judged.Count(j => j.Entry.Series is not null),
            judged.Count(j => j.Entry.AttributedVersionId is not null),
            grabbed,
            picked);
    }

    private static string Key(RssReleaseResponse entry) => $"{entry.IndexerId}:{entry.Guid}";

    private async Task<SeriesTitleIndex> BuildIndexAsync(CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking()
            .Select(s => new { s.Id, s.TvdbId, s.Title, s.OriginalTitle, s.Year, s.AlternativeTitles })
            .ToListAsync(ct);
        var tvdbIds = series.Where(s => s.TvdbId != null).Select(s => s.TvdbId!.Value).ToList();
        var aliases = await db.SceneNames.AsNoTracking().Where(n => tvdbIds.Contains(n.TvdbId)).ToListAsync(ct);
        return new SeriesTitleIndex(
            series.Select(s => new IndexedSeries(s.Id, s.TvdbId, s.Title, s.Year, new[] { s.OriginalTitle }.Concat(s.AlternativeTitles.Select(a => a.Title)).OfType<string>().ToList())),
            aliases.Select(a => new SceneAlias(a.TvdbId, a.Title, a.SceneSeasonNumber)));
    }

    private static RssReleaseResponse Entry(ReleaseInfo release, Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo? parsed, SeriesTargets? show, SeriesJudgement? judgement, string? reason, DateTime now)
    {
        var quality = parsed?.Quality?.Quality ?? ParserQuality.Unknown;
        var revision = parsed?.Quality?.Revision;
        var versions = (judgement?.Decisions ?? []).Select(d =>
        {
            var version = show!.Series.Versions.First(v => v.Id == ((EpisodeTarget)d.Target.Target).VersionId);
            return new VersionDecisionResponse(version.Id, version.Name, d.Approved, d.Rejections.Select(r => new RejectionResponse(r.Reason, r.Type == RejectionType.Temporary)).ToList(), d.CustomFormatScore);
        }).ToList();
        var attributed = judgement?.Attributed is { } a ? show!.Series.Versions.First(v => v.Id == ((EpisodeTarget)a.Target.Target).VersionId) : null;
        var series = show?.Series;

        return new RssReleaseResponse(
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
            null,
            string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup,
            parsed?.Languages?.Where(l => l.Id != 0).Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
            (judgement?.Formats ?? []).Select(f => f.Name).ToList(),
            string.IsNullOrWhiteSpace(parsed?.SeriesTitle) ? null : parsed!.SeriesTitle,
            parsed?.SeriesTitleInfo?.Year is > 0 ? parsed.SeriesTitleInfo.Year : null,
            null,
            series is not null && judgement?.IsMapped == true ? null : reason,
            versions,
            attributed?.Id,
            attributed?.Name,
            Series: series is null ? null : new RssSeriesRef(series.Id, series.Title, series.Year, series.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl),
            Episodes: judgement is { IsMapped: true } ? SeriesReleaseJudge.Label(judgement.Episodes) : null,
            EpisodeIds: judgement is { IsMapped: true } ? judgement.Episodes.Select(e => e.Id).ToList() : null);
    }
}
