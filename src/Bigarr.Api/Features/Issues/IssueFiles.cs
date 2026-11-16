using System.Globalization;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using Bigarr.Domain.Issues;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Issues;

/// <summary>A file an issue can point at, and the acquisition targets it fills (the film's version, or the episodes of a multi-episode file).</summary>
public sealed record IssueFile(IssueFileSnapshot Snapshot, List<int> TargetIds);

/// <summary>
/// Which file an issue is about and which release produced it: the import line in the history (release, indexer, download
/// id), and from it the grab, which is what the blocklist action blocks. A file that came from a scan or an import from
/// Radarr has no grab: its original release title, when known, is all there is.
/// </summary>
public sealed class IssueFiles(BigarrDbContext db)
{
    private static readonly HistoryEventType[] Imports = [HistoryEventType.Imported, HistoryEventType.Upgraded];

    /// <summary>The film version's file now; null when it has none.</summary>
    public async Task<IssueFile?> MovieAsync(int versionId, CancellationToken ct)
    {
        var file = await db.MediaFiles.AsNoTracking().Include(f => f.Quality).FirstOrDefaultAsync(f => f.VersionId == versionId, ct);
        if (file is null)
        {
            return null;
        }

        var import = await db.History.AsNoTracking()
            .Where(h => h.MediaFileId == file.Id && Imports.Contains(h.EventType))
            .OrderByDescending(h => h.Id)
            .FirstOrDefaultAsync(ct);
        var grab = await GrabAsync(import, [versionId], file.OriginalReleaseTitle, ct);
        return new IssueFile(
            new IssueFileSnapshot(file.Id, null, file.RelativePath, file.Size, file.Quality?.Name, import?.SourceTitle ?? grab?.ReleaseTitle ?? file.OriginalReleaseTitle, file.ReleaseGroup, import?.IndexerName ?? grab?.IndexerName, grab?.Id),
            [versionId]);
    }

    /// <summary>The episode's file in the version now; null when it has none.</summary>
    public async Task<IssueFile?> EpisodeAsync(int versionId, int episodeId, CancellationToken ct)
    {
        var file = await db.EpisodeFiles.AsNoTracking()
            .Include(f => f.Quality)
            .Where(f => f.Episodes.Any(t => t.VersionId == versionId && t.EpisodeId == episodeId))
            .FirstOrDefaultAsync(ct);
        if (file is null)
        {
            return null;
        }

        var targetIds = await db.EpisodeTargets.AsNoTracking().Where(t => t.EpisodeFileId == file.Id).Select(t => t.Id).ToListAsync(ct);

        // The series import line names its file in its data: read the latest few of these targets and match it there.
        var lines = await db.History.AsNoTracking()
            .Where(h => h.TargetId != null && targetIds.Contains(h.TargetId.Value) && Imports.Contains(h.EventType))
            .OrderByDescending(h => h.Id)
            .Take(50)
            .ToListAsync(ct);
        var fileId = file.Id.ToString(CultureInfo.InvariantCulture);
        var import = lines.FirstOrDefault(h => h.Data.GetValueOrDefault("episodeFileId") == fileId);
        var grab = await GrabAsync(import, targetIds, file.OriginalReleaseTitle, ct);
        return new IssueFile(
            new IssueFileSnapshot(null, file.Id, file.RelativePath, file.Size, file.Quality?.Name, import?.SourceTitle ?? grab?.ReleaseTitle ?? file.OriginalReleaseTitle, file.ReleaseGroup, import?.IndexerName ?? grab?.IndexerName, grab?.Id),
            targetIds);
    }

    /// <summary>The file an issue concerns now: the film's, or the episode's; none for a season or a whole series.</summary>
    public async Task<IssueFile?> CurrentAsync(Issue issue, CancellationToken ct) => issue.Scope switch
    {
        IssueScope.Movie when issue.MovieVersionId is { } versionId => await MovieAsync(versionId, ct),
        IssueScope.Episode when issue.SeriesVersionId is { } versionId && issue.EpisodeId is { } episodeId => await EpisodeAsync(versionId, episodeId, ct),
        _ => null,
    };

    /// <summary>The grab that produced a file: by the import's download id, else by the release title, on the file's targets.</summary>
    private async Task<Grab?> GrabAsync(HistoryEvent? import, List<int> targetIds, string? releaseTitle, CancellationToken ct)
    {
        var downloadId = import?.Data.GetValueOrDefault("downloadId");
        var grabs = db.Grabs.AsNoTracking().Where(g => g.Targets.Any(t => targetIds.Contains(t.TargetId)));
        if (!string.IsNullOrEmpty(downloadId))
        {
            var byDownload = await grabs.Where(g => g.DownloadId == downloadId).OrderByDescending(g => g.Id).FirstOrDefaultAsync(ct);
            if (byDownload is not null)
            {
                return byDownload;
            }
        }

        var title = import?.SourceTitle ?? releaseTitle;
        return string.IsNullOrEmpty(title) ? null : await grabs.Where(g => g.ReleaseTitle == title).OrderByDescending(g => g.Id).FirstOrDefaultAsync(ct);
    }
}
