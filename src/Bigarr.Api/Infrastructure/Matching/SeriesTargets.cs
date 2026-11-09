using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>
/// One series as the decision engine sees it: every version with its (version, episode) targets, each target's file
/// (or, in shadow mode, its latest pick), blocklist and active grab. <see cref="ContextsFor"/> turns the episodes a
/// release covers into one target context per version (blueprint §3.10: the engine never sees a <c>Series</c>).
/// </summary>
public sealed class SeriesTargets
{
    private readonly Dictionary<(int VersionId, int EpisodeId), EpisodeSlot> _slots;
    private readonly ILookup<int, BlocklistEntry> _blocklist;
    private readonly IReadOnlyDictionary<int, QualityDefinition> _definitions;
    private readonly IReadOnlyDictionary<int, string> _formatNames;
    private readonly DateTime _now;

    internal SeriesTargets(
        Series series,
        IReadOnlyList<SceneAlias> sceneNames,
        Dictionary<(int VersionId, int EpisodeId), EpisodeSlot> slots,
        ILookup<int, BlocklistEntry> blocklist,
        IReadOnlyDictionary<int, QualityDefinition> definitions,
        IReadOnlyDictionary<int, string> formatNames,
        DateTime now)
    {
        Series = series;
        SceneNames = sceneNames;
        _slots = slots;
        _blocklist = blocklist;
        _definitions = definitions;
        _formatNames = formatNames;
        _now = now;
        Episodes = series.Episodes.ToList();
        Mappable = MappableSeries.From(series);
    }

    public Series Series { get; }

    public IReadOnlyList<Episode> Episodes { get; }

    public MappableSeries Mappable { get; }

    /// <summary>XEM scene names of the show: query titles and lookup keys.</summary>
    public IReadOnlyList<SceneAlias> SceneNames { get; }

    public IEnumerable<SeriesVersion> Versions => Series.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id);

    public IndexedSeries Indexed => new(
        Series.Id,
        Series.TvdbId,
        Series.Title,
        Series.Year,
        new[] { Series.OriginalTitle }.Concat(Series.AlternativeTitles.Select(a => a.Title)).OfType<string>().ToList());

    public EpisodeSlot? Slot(int versionId, int episodeId) => _slots.GetValueOrDefault((versionId, episodeId));

    /// <summary>
    /// One context per version (default first) for the episodes a release covers. A version lacking a target for one
    /// of them (it should not happen: every version has one per episode) is left out.
    /// </summary>
    public List<TargetContext> ContextsFor(IReadOnlyList<Episode> covered, int? versionId = null)
    {
        ArgumentNullException.ThrowIfNull(covered);

        var contexts = new List<TargetContext>();
        if (covered.Count == 0)
        {
            return contexts;
        }

        foreach (var version in Versions)
        {
            if (versionId is not null && version.Id != versionId)
            {
                continue;
            }

            var slots = covered.Select(e => Slot(version.Id, e.Id)).ToList();
            if (slots.Any(s => s is null))
            {
                continue;
            }

            var present = slots.Select(s => s!).ToList();
            contexts.Add(new TargetContext(
                present[0].Target,
                version.QualityProfile,
                _definitions,
                null,
                Series.Runtime,
                present.SelectMany(s => _blocklist[s.Target.Id]).ToList(),
                present.Any(s => s.HasActiveGrab),
                true,
                version.IsDefault)
            {
                CustomFormatNames = _formatNames,
                Episodes = new EpisodeCoverage(present, Series.SeriesType == SeriesType.Anime, version.Monitored, Series.Runtime, _now),
            });
        }

        return contexts;
    }
}

/// <summary>
/// Loads <see cref="SeriesTargets"/>: the series graph, blocklist, active grabs and, in shadow mode, the latest pick of
/// each target made before the decision (a pick stands in for the file it would have become, as for films).
/// </summary>
public sealed class SeriesTargetsFactory(BigarrDbContext db, CustomFormatScorer scorer, IOptions<ShadowOptions> shadow, TimeProvider time)
{
    public IQueryable<Series> SeriesGraph() =>
        db.Series.AsNoTracking()
            .Include(s => s.Episodes)
            .Include(s => s.Versions).ThenInclude(v => v.QualityProfile).ThenInclude(p => p.FormatScores)
            .Include(s => s.Versions).ThenInclude(v => v.Episodes).ThenInclude(t => t.EpisodeFile!).ThenInclude(f => f.Quality)
            .AsSplitQuery();

    public async Task<SeriesTargets?> LoadAsync(int seriesId, CancellationToken ct)
    {
        var all = await LoadAsync([seriesId], time.GetUtcNow().UtcDateTime, ct);
        return all.GetValueOrDefault(seriesId);
    }

    /// <param name="asOf">Shadow mode: only picks made before this time stand in for files.</param>
    public async Task<Dictionary<int, SeriesTargets>> LoadAsync(IReadOnlyCollection<int> seriesIds, DateTime asOf, CancellationToken ct)
    {
        var result = new Dictionary<int, SeriesTargets>();
        if (seriesIds.Count == 0)
        {
            return result;
        }

        var series = await SeriesGraph().Where(s => seriesIds.Contains(s.Id)).ToListAsync(ct);
        if (series.Count == 0)
        {
            return result;
        }

        var targetIds = series.SelectMany(s => s.Versions).SelectMany(v => v.Episodes).Select(t => t.Id).ToList();
        var definitions = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, ct);
        var blocklist = (await db.Blocklist.AsNoTracking().Where(b => targetIds.Contains(b.TargetId)).ToListAsync(ct)).ToLookup(b => b.TargetId);
        var active = (await db.GrabTargets.AsNoTracking().Where(t => t.Active && targetIds.Contains(t.TargetId)).Select(t => t.TargetId).ToListAsync(ct)).ToHashSet();
        var calculator = await scorer.CalculatorAsync(ct);
        var formatNames = await scorer.NamesAsync(ct);
        var picks = shadow.Value.Enabled ? await LatestPicksAsync(targetIds, asOf, ct) : [];
        var tvdbIds = series.Where(s => s.TvdbId is not null).Select(s => s.TvdbId!.Value).ToList();
        var sceneNames = (await db.SceneNames.AsNoTracking().Where(n => tvdbIds.Contains(n.TvdbId)).ToListAsync(ct))
            .Select(n => new SceneAlias(n.TvdbId, n.Title, n.SceneSeasonNumber))
            .ToLookup(n => n.TvdbId);
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var show in series)
        {
            var episodes = show.Episodes.ToDictionary(e => e.Id);
            var slots = new Dictionary<(int, int), EpisodeSlot>();
            foreach (var version in show.Versions)
            {
                // No-tracking loads: link the graph by hand so each target resolves its version's profile.
                var fileEpisodes = version.Episodes.Where(t => t.EpisodeFileId is not null).ToLookup(t => t.EpisodeFileId!.Value, t => t.EpisodeId);
                foreach (var target in version.Episodes)
                {
                    target.Version = version;
                    if (!episodes.TryGetValue(target.EpisodeId, out var episode))
                    {
                        continue;
                    }

                    target.Episode = episode;
                    ExistingFile? existing = null;
                    if (picks.TryGetValue(target.Id, out var pick) && PickAsFile(pick, show, definitions, calculator) is { } picked)
                    {
                        existing = picked;
                    }
                    else if (target.EpisodeFile is { } file)
                    {
                        var matched = calculator.Match(CustomFormatScorer.EpisodeFileInput(
                            file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.ReleaseGroup, show.Year, show.OriginalLanguage));
                        existing = new ExistingFile(file.QualityId, file.Revision.Version, file.Revision.Real, file.Revision.IsRepack)
                        {
                            CustomFormatIds = matched.Select(m => m.Id).ToList(),
                            ReleaseGroup = file.ReleaseGroup,
                        };
                    }

                    slots[(version.Id, episode.Id)] = new EpisodeSlot(
                        target,
                        episode.Id,
                        episode.SeasonNumber,
                        episode.EpisodeNumber,
                        AirDateUtc(episode),
                        episode.Runtime,
                        existing,
                        active.Contains(target.Id))
                    {
                        FileEpisodeIds = existing is { IsPick: false } && target.EpisodeFileId is { } fileId ? fileEpisodes[fileId].ToList() : [],
                    };
                }
            }

            result[show.Id] = new SeriesTargets(
                show,
                show.TvdbId is { } tvdb ? sceneNames[tvdb].ToList() : [],
                slots,
                blocklist,
                definitions,
                formatNames,
                now);
        }

        return result;
    }

    /// <summary>Air time in UTC, else midnight of the local air date.</summary>
    private static DateTime? AirDateUtc(Episode episode) =>
        episode.AirDateUtc ?? episode.AirDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private async Task<Dictionary<int, SeriesShadowPick>> LatestPicksAsync(List<int> targetIds, DateTime before, CancellationToken ct)
    {
        var rows = await db.SeriesShadowPickEpisodes.AsNoTracking()
            .Where(e => e.TargetId != null && targetIds.Contains(e.TargetId.Value) && e.Pick.PickedAt < before)
            .Select(e => new { TargetId = e.TargetId!.Value, e.Pick })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TargetId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Pick).OrderByDescending(p => p.PickedAt).ThenByDescending(p => p.Id).First());
    }

    private static ExistingFile? PickAsFile(SeriesShadowPick pick, Series series, IReadOnlyDictionary<int, QualityDefinition> definitions, CustomFormatCalculator calculator)
    {
        if (pick.QualityId is not { } id || id == 0)
        {
            return null;
        }

        var formats = calculator.Match(CustomFormatScorer.EpisodeFileInput(
            pick.ReleaseTitle, pick.ReleaseTitle, pick.Size, pick.IndexerFlags, definitions.GetValueOrDefault(id), pick.Languages, pick.ReleaseGroup, series.Year, series.OriginalLanguage));
        return new ExistingFile(id, pick.RevisionVersion ?? 1, pick.RevisionReal ?? 0, pick.IsRepack)
        {
            CustomFormatIds = formats.Select(f => f.Id).ToList(),
            ReleaseGroup = pick.ReleaseGroup,
            IsPick = true,
        };
    }
}
