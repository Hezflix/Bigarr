using Bigarr.Domain.Library;

namespace Bigarr.Api.Features.Library;

public sealed record RootFolderResponse(
    int Id,
    string Path,
    bool Accessible,
    long? FreeSpace,
    long? TotalSpace,
    int VersionCount,
    int UnmappedFolderCount);

public sealed record AddRootFolderRequest(string Path);

public sealed record UnmappedFolderResponse(
    int Id,
    int RootFolderId,
    string RelativePath,
    string Path,
    int FileCount,
    UnmappedReason Reason,
    string? Detail,
    DateTime DiscoveredAt,
    DateTime LastSeenAt);

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
    string? Error);
