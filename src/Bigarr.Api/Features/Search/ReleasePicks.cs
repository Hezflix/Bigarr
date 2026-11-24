using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the film's versions already did with a release: grabbed it (still downloading, imported or failed), hold its
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
            .Select(t => new { t.TargetId, t.Grab.IndexerId, t.Grab.ReleaseGuid, t.Grab.InfoHash, t.Grab.ReleaseTitle, t.Grab.State, t.Grab.GrabbedAt })
            .ToListAsync(ct);
        foreach (var grab in grabs.OrderByDescending(g => g.GrabbedAt))
        {
            var kind = grab.State.IsActive() ? ReleasePickKind.Downloading
                : grab.State is GrabState.Imported ? ReleasePickKind.Imported
                : grab.State is GrabState.Ignored ? ReleasePickKind.Grabbed
                : ReleasePickKind.Failed;
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

    /// <summary>The picks of <paramref name="release"/>, at most one per version: on disk first, then the newest.</summary>
    public List<ReleasePickResponse> For(ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return _picks
            .Where(p => p.Key.Matches(release))
            .Select(p => p.Pick)
            .GroupBy(p => p.VersionId)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// The same release: same indexer and guid, or same info hash, or the same title (an indexer that changed its
    /// guids, a file imported from a download Bigarr did not grab).
    /// </summary>
    private sealed record PickKey(int? IndexerId, string? Guid, string? InfoHash, string? Title)
    {
        public bool Matches(ReleaseInfo release) =>
            (Guid is not null && IndexerId == release.IndexerId && string.Equals(Guid, release.Guid, StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(InfoHash) && string.Equals(InfoHash, release.InfoHash, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(Title) && string.Equals(Normalize(Title), Normalize(release.Title), StringComparison.OrdinalIgnoreCase));

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
