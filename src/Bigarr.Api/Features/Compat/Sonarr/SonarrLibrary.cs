using System.Linq.Expressions;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Compat.Sonarr;

using Bigarr.Domain.Tv;

/// <summary>One line of the Sonarr library: a series version and its show. The line's <c>id</c> is the version's.</summary>
public sealed record SonarrRow(Series Series, SeriesVersion Version);

/// <summary>Sonarr's statistics of one season, for one version: files and monitoring are the version's, the episode list is the show's.</summary>
public sealed record SeasonFigures(int EpisodeFileCount, int EpisodeCount, int TotalEpisodeCount, long SizeOnDisk, List<string> ReleaseGroups)
{
    public static readonly SeasonFigures Empty = new(0, 0, 0, 0, []);
}

/// <summary>
/// The API key's view over the series library, the twin of <see cref="Radarr.CompatLibrary"/>: <c>default</c> shows a
/// series as its default version, <c>all</c> one line per version, <c>version:&lt;name&gt;</c> only the versions of that
/// name. A show without a TVDB id is left out entirely: Seerr keys its library on <c>tvdbId</c> and Bazarr does
/// <c>int(tvdbId)</c> on every row, so a 0 would alias every such show into one.
/// </summary>
public sealed class SonarrLibrary(SeriesService series, BigarrDbContext db, TimeProvider time)
{
    public DateTime Now => time.GetUtcNow().UtcDateTime;

    public static bool Sees(CompatView view, SeriesVersion version)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(version);

        return view.Kind switch
        {
            CompatViewKind.Default => version.IsDefault,
            CompatViewKind.All => true,
            _ => string.Equals(version.Name, view.VersionName, StringComparison.Ordinal),
        };
    }

    /// <summary>The version the view designates in a show, or null: the default one, the named one; <c>all</c> designates nothing.</summary>
    public static SeriesVersion? Designated(CompatView view, Series show)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(show);

        return view.Kind switch
        {
            CompatViewKind.Default => show.Versions.FirstOrDefault(v => v.IsDefault),
            CompatViewKind.Version => show.Versions.FirstOrDefault(v => v.Name == view.VersionName),
            _ => null,
        };
    }

    /// <summary>
    /// The version an episode id is read through. Episode ids are shared by the versions of a show: a view that designates
    /// a version reads that one; <c>all</c> reads the default version (it designates none, and writes through it are refused).
    /// </summary>
    public static SeriesVersion? Reading(CompatView view, Series show)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(show);
        return view.Kind == CompatViewKind.All ? show.Versions.FirstOrDefault(v => v.IsDefault) : Designated(view, show);
    }

    public static Expression<Func<SeriesVersion, bool>> Predicate(CompatView view) => view.Kind switch
    {
        CompatViewKind.Default => v => v.IsDefault,
        CompatViewKind.All => v => true,
        _ => v => v.Name == view.VersionName,
    };

    private IQueryable<Series> Shows() => series.WithGraph().Where(s => s.TvdbId != null && s.TvdbId > 0);

    /// <summary>Every line of the view, shows sorted by title. <paramref name="tvdbId"/> narrows to one show (Seerr's orphan check).</summary>
    public async Task<List<SonarrRow>> RowsAsync(CompatView view, int? tvdbId, CancellationToken ct)
    {
        var predicate = Predicate(view);
        var query = Shows().Where(s => s.Versions.AsQueryable().Any(predicate));
        if (tvdbId is not null)
        {
            query = query.Where(s => s.TvdbId == tvdbId);
        }

        var shows = await query.OrderBy(s => s.SortTitle).ThenBy(s => s.Year).ToListAsync(ct);
        return shows.SelectMany(s => s.Versions.Where(v => Sees(view, v)).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).Select(v => new SonarrRow(s, v))).ToList();
    }

    /// <summary>The line behind a compat series id, or null when the version is unknown, outside the view, or its show has no TVDB id.</summary>
    public async Task<SonarrRow?> FindAsync(CompatView view, int versionId, CancellationToken ct)
    {
        var show = await Shows().FirstOrDefaultAsync(s => s.Versions.Any(v => v.Id == versionId), ct);
        var version = show?.Versions.FirstOrDefault(v => v.Id == versionId);
        return show is null || version is null || !Sees(view, version) ? null : new SonarrRow(show, version);
    }

    public Task<Series?> FindShowByTvdbAsync(int tvdbId, CancellationToken ct) =>
        series.WithGraph().FirstOrDefaultAsync(s => s.TvdbId == tvdbId, ct);

    public Task<Series?> FindShowAsync(int seriesId, CancellationToken ct) =>
        Shows().FirstOrDefaultAsync(s => s.Id == seriesId, ct);

    /// <summary>The series version ids the view shows, for filtering the queue without loading shows.</summary>
    public async Task<HashSet<int>> VisibleVersionIdsAsync(CompatView view, CancellationToken ct)
    {
        var predicate = Predicate(view);
        var ids = await db.SeriesVersions.AsNoTracking()
            .Where(v => v.Series.TvdbId != null && v.Series.TvdbId > 0)
            .Where(predicate)
            .Select(v => v.Id)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    /// <summary>
    /// Per (version, season): episodes with a file, monitored aired episodes plus those with a file (Sonarr's
    /// <c>episodeCount</c>), every episode of the season aired or not (<c>totalEpisodeCount</c>, what Seerr compares the
    /// file count with), size and release groups of the files. Two grouped reads for any number of versions.
    /// </summary>
    public async Task<Dictionary<(int VersionId, int Season), SeasonFigures>> StatisticsAsync(IReadOnlyCollection<int> versionIds, CancellationToken ct)
    {
        var ids = versionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var now = Now;
        var counts = await db.EpisodeTargets.AsNoTracking()
            .Where(t => ids.Contains(t.VersionId))
            .GroupBy(t => new { t.VersionId, t.Episode.SeasonNumber })
            .Select(g => new
            {
                g.Key.VersionId,
                g.Key.SeasonNumber,
                Total = g.Count(),
                Files = g.Count(t => t.EpisodeFileId != null),
                Counted = g.Count(t => t.EpisodeFileId != null || (t.Monitored && t.Episode.AirDateUtc != null && t.Episode.AirDateUtc <= now)),
            })
            .ToListAsync(ct);
        var files = (await db.EpisodeFiles.AsNoTracking()
                .Where(f => ids.Contains(f.VersionId))
                .Select(f => new { f.VersionId, f.SeasonNumber, f.Size, f.ReleaseGroup })
                .ToListAsync(ct))
            .ToLookup(f => (f.VersionId, f.SeasonNumber));

        var result = new Dictionary<(int, int), SeasonFigures>();
        foreach (var c in counts)
        {
            var mine = files[(c.VersionId, c.SeasonNumber)].ToList();
            var groups = mine.Select(f => f.ReleaseGroup).OfType<string>().Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            result[(c.VersionId, c.SeasonNumber)] = new SeasonFigures(c.Files, c.Counted, c.Total, mine.Sum(f => f.Size), groups);
        }

        return result;
    }

    /// <summary>The version's (version, episode) pairs with their episode and file, in TVDB order.</summary>
    public Task<List<EpisodeTarget>> EpisodesAsync(int versionId, CancellationToken ct) =>
        Targets().Where(t => t.VersionId == versionId)
            .OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber)
            .ToListAsync(ct);

    public Task<List<EpisodeTarget>> EpisodesAsync(int versionId, IReadOnlyCollection<int> episodeIds, CancellationToken ct) =>
        Targets().Where(t => t.VersionId == versionId && episodeIds.Contains(t.EpisodeId))
            .OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber)
            .ToListAsync(ct);

    /// <summary>The show an episode belongs to, or null for an unknown episode.</summary>
    public async Task<int?> SeriesOfEpisodeAsync(int episodeId, CancellationToken ct) =>
        await db.Episodes.AsNoTracking().Where(e => e.Id == episodeId).Select(e => (int?)e.SeriesId).FirstOrDefaultAsync(ct);

    /// <summary>The shows a set of episodes belong to, by episode id.</summary>
    public async Task<Dictionary<int, int>> SeriesOfEpisodesAsync(IReadOnlyCollection<int> episodeIds, CancellationToken ct) =>
        await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.SeriesId, ct);

    /// <summary>An episode file with the episodes it holds, or null.</summary>
    public Task<EpisodeFile?> FileAsync(int fileId, CancellationToken ct) =>
        Files().FirstOrDefaultAsync(f => f.Id == fileId, ct);

    public Task<List<EpisodeFile>> FilesAsync(int versionId, CancellationToken ct) =>
        Files().Where(f => f.VersionId == versionId).OrderBy(f => f.SeasonNumber).ThenBy(f => f.RelativePath).ToListAsync(ct);

    public Task<List<EpisodeFile>> FilesAsync(IReadOnlyCollection<int> fileIds, CancellationToken ct) =>
        Files().Where(f => fileIds.Contains(f.Id)).OrderBy(f => f.Id).ToListAsync(ct);

    /// <summary>The (version, episode) pairs behind history targets, by target id.</summary>
    public async Task<Dictionary<int, (int VersionId, int EpisodeId)>> TargetsAsync(IReadOnlyCollection<int> targetIds, CancellationToken ct)
    {
        var rows = await db.EpisodeTargets.AsNoTracking().Where(t => targetIds.Contains(t.Id)).Select(t => new { t.Id, t.VersionId, t.EpisodeId }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.VersionId, r.EpisodeId));
    }

    private IQueryable<EpisodeTarget> Targets() =>
        db.EpisodeTargets.AsNoTracking()
            .Include(t => t.Episode)
            .Include(t => t.EpisodeFile).ThenInclude(f => f!.Quality)
            .AsSplitQuery();

    private IQueryable<EpisodeFile> Files() =>
        db.EpisodeFiles.AsNoTracking()
            .Include(f => f.Quality)
            .Include(f => f.Episodes)
            .AsSplitQuery();
}
