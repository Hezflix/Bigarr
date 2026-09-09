namespace Bigarr.Api.Features.Library;

/// <summary>Mutable, in-memory progress of one root folder scan. Read by the API while the scanner writes it.</summary>
public sealed class ScanProgress(int rootFolderId)
{
    private int _foldersTotal;
    private int _foldersProcessed;
    private int _moviesAdded;
    private int _versionsAdded;
    private int _filesAdded;
    private int _unmapped;

    public int RootFolderId { get; } = rootFolderId;

    public ScanState State { get; set; } = ScanState.Queued;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? CurrentFolder { get; set; }

    public string? Error { get; set; }

    public int FoldersTotal { get => _foldersTotal; set => _foldersTotal = value; }

    public int FoldersProcessed => _foldersProcessed;

    public int MoviesAdded => _moviesAdded;

    public int VersionsAdded => _versionsAdded;

    public int FilesAdded => _filesAdded;

    public int Unmapped => _unmapped;

    public void FolderDone() => Interlocked.Increment(ref _foldersProcessed);

    public void MovieAdded() => Interlocked.Increment(ref _moviesAdded);

    public void VersionAdded() => Interlocked.Increment(ref _versionsAdded);

    public void FileAdded() => Interlocked.Increment(ref _filesAdded);

    public void FolderUnmapped() => Interlocked.Increment(ref _unmapped);

    public ScanStatusResponse ToResponse() => new(
        RootFolderId, State, StartedAt, FinishedAt, FoldersTotal, FoldersProcessed, MoviesAdded, VersionsAdded, FilesAdded, Unmapped, CurrentFolder, Error);
}
