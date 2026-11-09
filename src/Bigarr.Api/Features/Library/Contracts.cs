using Bigarr.Domain.Library;

namespace Bigarr.Api.Features.Library;

public sealed record RootFolderResponse(
    int Id,
    string Path,
    bool Accessible,
    long? FreeSpace,
    long? TotalSpace,
    int VersionCount,
    int UnmappedFolderCount,
    /// <summary>Films or series. The scan, the add pages and the Radarr API only see roots of their kind.</summary>
    MediaKind Kind = MediaKind.Movies);

public sealed record AddRootFolderRequest(string Path, MediaKind Kind = MediaKind.Movies);

public sealed record UpdateRootFolderRequest(MediaKind Kind);

public sealed record UnmappedFolderResponse(
    int Id,
    int RootFolderId,
    string RelativePath,
    string Path,
    int FileCount,
    UnmappedReason Reason,
    string? Detail,
    DateTime DiscoveredAt,
    DateTime LastSeenAt,
    /// <summary>The root's kind: a film folder goes through the manual import, a series folder is mapped to a show.</summary>
    MediaKind Kind = MediaKind.Movies);

public enum ScanState
{
    Idle = 0,
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
}

public sealed record ScanStatusResponse(
    int RootFolderId,
    ScanState State,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int FoldersTotal,
    int FoldersProcessed,
    int MoviesAdded,
    int VersionsAdded,
    int FilesAdded,
    int Unmapped,
    string? CurrentFolder,
    string? Error,
    int SeriesAdded = 0,
    /// <summary>Video files inside a series folder the scan could not attach to episodes (kept, listed per series).</summary>
    int UnmatchedFiles = 0,
    /// <summary>Set on a single-series rescan.</summary>
    int? SeriesId = null);
