using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the film's (or the series') versions already did with a release: grabbed it (still downloading, imported or failed), hold its
/// file, or picked it in shadow mode. The interactive search shows it on the release's row, so a search run again after a
/// grab finds the release that was picked.
/// </summary>
public sealed class ReleasePicks
{
    private readonly List<(PickKey Key, ReleasePickResponse Pick)> _picks;

    private ReleasePicks(List<(PickKey, ReleasePickResponse)> picks) => _picks = picks;

    /// <summary>Every grab, shadow pick and file of the film's versions, newest first.</summary>
    public static async Task<ReleasePicks> LoadAsync(BigarrDbContext db, Movie movie, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(movie);
        var names = movie.Versions.ToDictionary(v => v.Id, v => v.Name);
        var ids = names.Keys.ToList();
        var picks = new List<(PickKey, ReleasePickResponse)>();

        // The file in place came from this release: the strongest link, whatever happened to its grab.
        foreach (var version in movie.Versions.Where(v => !string.IsNullOrWhiteSpace(v.File?.OriginalReleaseTitle)))
        {
            picks.Add((new PickKey(null, null, null, version.File!.OriginalReleaseTitle), new ReleasePickResponse(version.Id, version.Name, ReleasePickKind.OnDisk, version.File.DateAdded)));
        }

        var grabs = await db.GrabTargets.AsNoTracking()
            .Where(t => ids.Contains(t.TargetId))
            .Select(t => new { t.GrabId, t.TargetId, t.Grab.IndexerId, t.Grab.ReleaseGuid, t.Grab.InfoHash, t.Grab.ReleaseTitle, t.Grab.State, t.Grab.GrabbedAt })
            .ToListAsync(ct);
        var added = movie.Versions.Where(v => v.File is not null).ToDictionary(v => v.Id, v => v.File!.DateAdded);
        var onDisk = OnDisk(grabs.Select(g => (g.GrabId, g.TargetId, g.State, g.GrabbedAt, added.TryGetValue(g.TargetId, out var at) ? at : (DateTime?)null)));
        foreach (var grab in grabs.OrderByDescending(g => g.GrabbedAt))
        {
            var kind = onDisk.Contains((grab.GrabId, grab.TargetId)) ? ReleasePickKind.OnDisk : KindOf(grab.State);
            picks.Add((new PickKey(grab.IndexerId, grab.ReleaseGuid, grab.InfoHash, grab.ReleaseTitle), new ReleasePickResponse(grab.TargetId, names[grab.TargetId], kind, grab.GrabbedAt)));
        }

        var shadow = await db.ShadowPicks.AsNoTracking()
            .Where(p => p.VersionId != null && ids.Contains(p.VersionId.Value))
            .Select(p => new { VersionId = p.VersionId!.Value, p.IndexerId, p.ReleaseGuid, p.InfoHash, p.ReleaseTitle, p.LastPickedAt })
            .ToListAsync(ct);
        foreach (var pick in shadow.OrderByDescending(p => p.LastPickedAt))
        {
            picks.Add((new PickKey(pick.IndexerId, pick.ReleaseGuid, pick.InfoHash, pick.ReleaseTitle), new ReleasePickResponse(pick.VersionId, names[pick.VersionId], ReleasePickKind.Shadow, pick.LastPickedAt)));
        }

        return new ReleasePicks(picks);
    }

    /// <summary>
    /// Every grab, shadow pick and file of the series' versions, newest first. A pack grabbed for many episodes counts
    /// once per version; a file counts once per version and release.
    /// </summary>
    public static async Task<ReleasePicks> LoadAsync(BigarrDbContext db, int seriesId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var picks = new List<(PickKey, ReleasePickResponse)>();

        var files = await db.EpisodeTargets.AsNoTracking()
            .Where(t => t.Version.SeriesId == seriesId && t.EpisodeFile != null && t.EpisodeFile.OriginalReleaseTitle != null)
            .Select(t => new { t.VersionId, VersionName = t.Version.Name, Title = t.EpisodeFile!.OriginalReleaseTitle!, t.EpisodeFile.DateAdded })
            .ToListAsync(ct);
        foreach (var file in files.DistinctBy(f => (f.VersionId, f.Title)).OrderByDescending(f => f.DateAdded))
        {
            picks.Add((new PickKey(null, null, null, file.Title), new ReleasePickResponse(file.VersionId, file.VersionName, ReleasePickKind.OnDisk, file.DateAdded)));
        }

        var grabs = await db.GrabTargets.AsNoTracking()
            .Join(db.EpisodeTargets.Where(t => t.Version.SeriesId == seriesId), g => g.TargetId, t => t.Id, (g, t) => new { t.VersionId, VersionName = t.Version.Name, g.GrabId, g.TargetId, FileAdded = t.EpisodeFile == null ? (DateTime?)null : t.EpisodeFile.DateAdded, g.Grab.IndexerId, g.Grab.ReleaseGuid, g.Grab.InfoHash, g.Grab.ReleaseTitle, g.Grab.State, g.Grab.GrabbedAt })
            .ToListAsync(ct);
        // A pack's files carry their own names, not the pack's: an imported grab still in place says "on disk" for them.
        var onDisk = OnDisk(grabs.Select(g => (g.GrabId, g.TargetId, g.State, g.GrabbedAt, g.FileAdded)));
        foreach (var grab in grabs.GroupBy(g => (g.GrabId, g.VersionId)).Select(g => (First: g.First(), OnDisk: g.Any(t => onDisk.Contains((t.GrabId, t.TargetId))))).OrderByDescending(g => g.First.GrabbedAt))
        {
            var kind = grab.OnDisk ? ReleasePickKind.OnDisk : KindOf(grab.First.State);
            picks.Add((new PickKey(grab.First.IndexerId, grab.First.ReleaseGuid, grab.First.InfoHash, grab.First.ReleaseTitle), new ReleasePickResponse(grab.First.VersionId, grab.First.VersionName, kind, grab.First.GrabbedAt)));
        }

        var shadow = await db.SeriesShadowPicks.AsNoTracking()
            .Where(p => p.VersionId != null && p.SeriesId == seriesId)
            .Select(p => new { VersionId = p.VersionId!.Value, p.VersionName, p.IndexerId, p.ReleaseGuid, p.InfoHash, p.ReleaseTitle, p.LastPickedAt })
            .ToListAsync(ct);
        foreach (var pick in shadow.OrderByDescending(p => p.LastPickedAt))
        {
            picks.Add((new PickKey(pick.IndexerId, pick.ReleaseGuid, pick.InfoHash, pick.ReleaseTitle), new ReleasePickResponse(pick.VersionId, pick.VersionName, ReleasePickKind.Shadow, pick.LastPickedAt)));
        }

        return new ReleasePicks(picks);
    }

    /// <summary>
    /// The imported grabs whose file is still in place: for each target holding a file, the last grab imported before
    /// the file was added. A file put in by hand after it is taken for it too: nothing on the file names its grab.
    /// </summary>
    private static HashSet<(int GrabId, int TargetId)> OnDisk(IEnumerable<(int GrabId, int TargetId, GrabState State, DateTime GrabbedAt, DateTime? FileAdded)> grabs) =>
        grabs
            .Where(g => g.State is GrabState.Imported && g.FileAdded is { } added && g.GrabbedAt <= added)
            .GroupBy(g => g.TargetId)
            .Select(g => g.MaxBy(x => x.GrabbedAt))
            .Select(g => (g.GrabId, g.TargetId))
            .ToHashSet();

    private static ReleasePickKind KindOf(GrabState state) =>
        state.IsActive() ? ReleasePickKind.Downloading
        : state is GrabState.Imported ? ReleasePickKind.Imported
        : state is GrabState.Ignored ? ReleasePickKind.Grabbed
        : ReleasePickKind.Failed;

    /// <summary>The picks of <paramref name="release"/>, at most one per version: on disk first, then the newest.</summary>
    public List<ReleasePickResponse> For(int indexerId, string guid, string? infoHash, string title) =>
        _picks
            .Where(p => p.Key.Matches(indexerId, guid, infoHash, title))
            .Select(p => p.Pick)
            .GroupBy(p => p.VersionId)
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// The same release: same indexer and guid, or same info hash, or the same title (an indexer that changed its
    /// guids, a file imported from a download Bigarr did not grab).
    /// </summary>
    private sealed record PickKey(int? IndexerId, string? Guid, string? InfoHash, string? Title)
    {
        public bool Matches(int indexerId, string guid, string? infoHash, string title) =>
            (Guid is not null && IndexerId == indexerId && string.Equals(Guid, guid, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(InfoHash) && string.Equals(InfoHash, infoHash, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(Title) && string.Equals(Normalize(Title), Normalize(title), StringComparison.OrdinalIgnoreCase));

        // A file keeps the release's name with its extension, and indexers swap dots and spaces.
        private static string Normalize(string title)
        {
            var trimmed = title.Trim();
            foreach (var extension in new[] { ".mkv", ".mp4", ".avi", ".m2ts", ".ts" })
            {
                if (trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[..^extension.Length];
                    break;
                }
            }

            return trimmed.Replace(' ', '.');
        }
    }
}
