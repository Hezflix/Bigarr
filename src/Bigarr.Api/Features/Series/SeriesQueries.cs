namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>Read side of the slice: projections and per-version statistics computed in SQL, never stored.</summary>
public sealed class SeriesQueries(BigarrDbContext db, TimeProvider time)
{
    private sealed record StatRow(int VersionId, int SeasonNumber, int Total, int Monitored, int Files, int Counted);

    private sealed record SizeRow(int VersionId, int SeasonNumber, long Size, int Files);

    /// <summary>
    /// The library list: one light row per series, with what the library page sorts and filters on. Each figure is one
    /// grouped read over the whole library (targets, files, active grabs), joined in memory by version id: the per-season
    /// statistics of the series page would read every (version, season) pair and match them one by one.
    /// </summary>
    public async Task<List<SeriesSummary>> ListAsync(CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking()
            .OrderBy(s => s.SortTitle)
            .Select(s => new
            {
                s.Id,
                s.TvdbId,
                s.TmdbId,
                s.Title,
                s.SortTitle,
                s.Year,
                s.Status,
                s.Network,
                s.SeriesType,
                s.Images,
                s.AddedAt,
                SeasonCount = s.Seasons.Count(x => x.SeasonNumber > 0),
                Versions = s.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id)
                    .Select(v => new VersionRow(
                        v.Id, v.Name, v.Mode, v.IsDefault, v.Monitored, v.QualityProfileId, v.QualityProfile.Name, v.RootFolderId, v.MonitorNewItems,
                        v.Tags.Select(t => t.Id).ToList()))
                    .ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        // Sonarr's series counts: specials stay out of the episode figures, every file counts in the size.
        var counts = await db.EpisodeTargets.AsNoTracking()
            .Where(t => t.Episode.SeasonNumber > 0)
            .GroupBy(t => t.VersionId)
            .Select(g => new
            {
                VersionId = g.Key,
                Total = g.Count(),
                Monitored = g.Count(t => t.Monitored),
                Files = g.Count(t => t.EpisodeFileId != null),
                Counted = g.Count(t => t.EpisodeFileId != null || (t.Monitored && t.Episode.AirDateUtc != null && t.Episode.AirDateUtc <= now)),
            })
            .ToDictionaryAsync(x => x.VersionId, ct);
        var files = (await db.EpisodeFiles.AsNoTracking()
                .GroupBy(f => new { f.VersionId, f.QualityId })
                .Select(g => new { g.Key.VersionId, g.Key.QualityId, Size = g.Sum(f => f.Size) })
                .ToListAsync(ct))
            .ToLookup(f => f.VersionId);
        var downloading = (await db.GrabTargets.AsNoTracking()
                .Where(g => g.Active)
                .Join(db.EpisodeTargets, g => g.TargetId, t => t.Id, (g, t) => t.VersionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var nextAiring = await db.Episodes.AsNoTracking()
            .Where(e => e.AirDate != null && e.AirDate >= today && e.SeasonNumber > 0)
            .GroupBy(e => e.SeriesId)
            .Select(g => new { SeriesId = g.Key, Next = g.Min(e => e.AirDate) })
            .ToDictionaryAsync(x => x.SeriesId, x => x.Next, ct);
        var profiles = await db.QualityProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var qualityNames = await db.QualityDefinitions.AsNoTracking().ToDictionaryAsync(q => q.Id, q => q.Name, ct);

        SeriesVersionSummary Summary(VersionRow v)
        {
            var count = counts.GetValueOrDefault(v.Id);
            var mine = files[v.Id].ToList();
            var statistics = new EpisodeStatistics(count?.Counted ?? 0, count?.Files ?? 0, count?.Monitored ?? 0, count?.Total ?? 0, mine.Sum(f => f.Size));
            var profile = profiles.GetValueOrDefault(v.QualityProfileId);
            var qualities = mine
                .Select(f => f.QualityId)
                .Distinct()
                .OrderByDescending(id => profile is null ? -1 : ProfileRanking.RankOf(profile, id))
                .Select(id => qualityNames.GetValueOrDefault(id, $"#{id}"))
                .ToList();
            var status = downloading.Contains(v.Id) ? VersionStatus.Downloading
                : statistics.EpisodeCount > statistics.EpisodeFileCount ? VersionStatus.Missing
                : mine.Count > 0 ? VersionStatus.Available
                : VersionStatus.Pending;

            // Quality only, as a one-shot version is never upgraded: the custom format score of every episode file is
            // the series page's business, not the list's.
            bool? cutoffMet = mine.Count == 0 || v.Mode != VersionMode.Managed || profile is null ? null : mine.All(f => ProfileRanking.CutoffMet(profile, f.QualityId));
            return new SeriesVersionSummary(
                v.Id, v.Name, v.IsDefault, v.Monitored, v.QualityProfileId, v.Profile, statistics,
                v.Mode, v.RootFolderId, v.TagIds.Order().ToList(), v.MonitorNewItems, status, qualities, cutoffMet);
        }

        return series.Select(s => new SeriesSummary(
            s.Id,
            s.TvdbId,
            s.TmdbId,
            s.Title,
            s.SortTitle,
            s.Year,
            s.Status,
            s.Network,
            s.SeriesType,
            s.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            s.AddedAt,
            s.Versions.Any(v => v.Monitored),
            s.SeasonCount,
            nextAiring.GetValueOrDefault(s.Id),
            s.Versions.Select(Summary).ToList())).ToList();
    }

    private sealed record VersionRow(
        int Id,
        string Name,
        VersionMode Mode,
        bool IsDefault,
        bool Monitored,
        int QualityProfileId,
        string Profile,
        int RootFolderId,
        NewItemMonitoring MonitorNewItems,
        List<int> TagIds);

    public async Task<SeriesResponse?> GetAsync(int seriesId, CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking()
            .Include(s => s.Seasons)
            .Include(s => s.Versions).ThenInclude(v => v.QualityProfile)
            .Include(s => s.Versions).ThenInclude(v => v.RootFolder)
            .Include(s => s.Versions).ThenInclude(v => v.Tags)
            .Include(s => s.Versions).ThenInclude(v => v.Seasons)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return null;
        }

        var versions = series.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToList();
        var stats = await StatisticsAsync(versions.Select(v => v.Id).ToList(), ct);
        var now = time.GetUtcNow().UtcDateTime;
        var episodeCounts = await db.Episodes.AsNoTracking()
            .Where(e => e.SeriesId == seriesId)
            .GroupBy(e => e.SeasonNumber)
            .Select(g => new { Season = g.Key, Total = g.Count(), Aired = g.Count(e => e.AirDateUtc != null && e.AirDateUtc <= now) })
            .ToDictionaryAsync(x => x.Season, ct);

        var seasons = series.Seasons.OrderBy(s => s.SeasonNumber).Select(season => new SeasonResponse(
            season.SeasonNumber,
            season.Title,
            season.Overview,
            season.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            episodeCounts.GetValueOrDefault(season.SeasonNumber)?.Total ?? 0,
            episodeCounts.GetValueOrDefault(season.SeasonNumber)?.Aired ?? 0,
            versions.Select(v => new SeasonVersionState(
                v.Id,
                v.Seasons.FirstOrDefault(f => f.SeasonId == season.Id)?.Monitored ?? false,
                stats.GetValueOrDefault((v.Id, season.SeasonNumber)) ?? EpisodeStatistics.Empty)).ToList())).ToList();

        return new SeriesResponse(
            series.Id,
            series.TvdbId,
            series.TmdbId,
            series.ImdbId,
            series.Title,
            series.OriginalTitle,
            series.SortTitle,
            series.Year,
            series.Overview,
            series.Status,
            series.Network,
            series.Runtime,
            series.AirTime,
            series.OriginalCountry,
            series.OriginalLanguage,
            series.SeriesType,
            series.Ordering,
            series.Genres,
            series.AlternativeTitles,
            series.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            series.Images.FirstOrDefault(i => i.Type == ImageType.Fanart)?.RemoteUrl,
            series.FirstAired,
            series.LastAired,
            series.DefaultRootFolderId,
            series.AddedAt,
            series.LastMetadataRefreshAt,
            series.Monitored,
            seasons,
            versions.Select(v => ToVersion(v, Total(stats, v.Id))).ToList(),
            series.EpisodeGroupId,
            series.EpisodeGroupName,
            series.EpisodeGroupType,
            series.OrderOnlyEpisodes);
    }

    public async Task<List<EpisodeResponse>?> EpisodesAsync(int seriesId, int? seasonNumber, CancellationToken ct)
    {
        var ordering = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => (EpisodeOrder?)s.Ordering).FirstOrDefaultAsync(ct);
        if (ordering is not { } order)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var query = db.Episodes.AsNoTracking().Where(e => e.SeriesId == seriesId);
        if (seasonNumber is { } season)
        {
            query = query.Where(e => e.SeasonNumber == season);
        }

        var rows = await query
            .OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => new
            {
                Episode = e,
                Targets = e.Targets.OrderBy(t => t.VersionId).Select(t => new EpisodeVersionState(t.VersionId, t.Id, t.Monitored, t.EpisodeFileId != null, t.EpisodeFileId)).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new EpisodeResponse(
            r.Episode.Id,
            r.Episode.TvdbId,
            r.Episode.TmdbId,
            r.Episode.SeasonNumber,
            r.Episode.EpisodeNumber,
            r.Episode.AbsoluteEpisodeNumber,
            r.Episode.AirDate,
            r.Episode.AirDateUtc,
            r.Episode.Title,
            r.Episode.Overview,
            r.Episode.Runtime,
            r.Episode.ImageUrl,
            r.Episode.IsSpecial,
            r.Episode.HasAired(now),
            r.Targets,
            r.Episode.OrderSeasonNumber,
            r.Episode.OrderEpisodeNumber,
            r.Episode.OrderAbsoluteNumber,
            r.Episode.DisplaySeasonNumber(order),
            r.Episode.DisplayEpisodeNumber(order),
            r.Episode.DisplayAbsoluteNumber(order),
            order != EpisodeOrder.Tvdb && !r.Episode.InOrder)).ToList();
    }

    public static SeriesVersionResponse ToVersion(SeriesVersion version, EpisodeStatistics statistics) => new(
        version.Id,
        version.Name,
        version.Mode,
        version.QualityProfileId,
        version.QualityProfile.Name,
        version.IsDefault,
        version.Monitored,
        version.SeasonFolder,
        version.MonitorNewItems,
        version.RootFolderId,
        version.RootFolder.Path,
        version.FolderName,
        Path.Combine(version.RootFolder.Path, version.FolderName),
        version.CreatedAt,
        version.Tags.Select(t => t.Id).Order().ToList(),
        statistics,
        Movies.MovieMapper.ToConstraints(version.Constraints));

    public async Task<SeriesVersionResponse?> GetVersionAsync(int seriesId, int versionId, CancellationToken ct)
    {
        var version = await db.SeriesVersions.AsNoTracking()
            .Include(v => v.QualityProfile)
            .Include(v => v.RootFolder)
            .Include(v => v.Tags)
            .FirstOrDefaultAsync(v => v.Id == versionId && v.SeriesId == seriesId, ct);
        return version is null ? null : ToVersion(version, Total(await StatisticsAsync([versionId], ct), versionId));
    }

    /// <summary>
    /// Per (version, season): Sonarr's counts. <c>EpisodeCount</c> = monitored and aired, or with a file. The version
    /// totals (<see cref="Total"/>) leave specials out, except for files and size which count every file.
    /// </summary>
    private async Task<Dictionary<(int VersionId, int SeasonNumber), EpisodeStatistics>> StatisticsAsync(List<int> versionIds, CancellationToken ct)
    {
        if (versionIds.Count == 0)
        {
            return [];
        }

        var now = time.GetUtcNow().UtcDateTime;
        var rows = await db.EpisodeTargets.AsNoTracking()
            .Where(t => versionIds.Contains(t.VersionId))
            .GroupBy(t => new { t.VersionId, t.Episode.SeasonNumber })
            .Select(g => new StatRow(
                g.Key.VersionId,
                g.Key.SeasonNumber,
                g.Count(),
                g.Count(t => t.Monitored),
                g.Count(t => t.EpisodeFileId != null),
                g.Count(t => t.EpisodeFileId != null || (t.Monitored && t.Episode.AirDateUtc != null && t.Episode.AirDateUtc <= now))))
            .ToListAsync(ct);

        var sizes = await db.EpisodeFiles.AsNoTracking()
            .Where(f => versionIds.Contains(f.VersionId))
            .GroupBy(f => new { f.VersionId, f.SeasonNumber })
            .Select(g => new SizeRow(g.Key.VersionId, g.Key.SeasonNumber, g.Sum(f => f.Size), g.Count()))
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => (r.VersionId, r.SeasonNumber),
            r => new EpisodeStatistics(r.Counted, r.Files, r.Monitored, r.Total, sizes.FirstOrDefault(s => s.VersionId == r.VersionId && s.SeasonNumber == r.SeasonNumber)?.Size ?? 0));
    }

    private static EpisodeStatistics Total(Dictionary<(int VersionId, int SeasonNumber), EpisodeStatistics> stats, int versionId)
    {
        var mine = stats.Where(s => s.Key.VersionId == versionId).ToList();
        var regular = mine.Where(s => s.Key.SeasonNumber > 0).Select(s => s.Value).ToList();
        return new EpisodeStatistics(
            regular.Sum(s => s.EpisodeCount),
            // Like Sonarr's series progress: specials stay out of both counts (they are in the size).
            regular.Sum(s => s.EpisodeFileCount),
            regular.Sum(s => s.MonitoredEpisodeCount),
            regular.Sum(s => s.TotalEpisodeCount),
            mine.Sum(s => s.Value.SizeOnDisk));
    }
}
