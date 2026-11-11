namespace Bigarr.Domain.Downloads;

/// <summary>
/// Protocol implementation for one <see cref="DownloadClientImplementation"/>. Stateless apart from sessions: the
/// <see cref="DownloadClient"/> row carries the configuration.
/// </summary>
public interface IDownloadClient
{
    DownloadClientImplementation Implementation { get; }

    /// <summary>Connects, reads the version and makes sure the category exists.</summary>
    Task<DownloadClientInfo> TestAsync(DownloadClient client, CancellationToken cancellationToken);

    /// <summary>Adds the release and returns the identifier the client will list it under (the info hash, lower case).</summary>
    Task<string> AddAsync(DownloadClient client, DownloadRequest request, CancellationToken cancellationToken);

    /// <summary>Everything in the client's categories (films and series), tracked by Bigarr or not.</summary>
    Task<IReadOnlyList<DownloadItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken);

    Task RemoveAsync(DownloadClient client, string downloadId, bool deleteFiles, CancellationToken cancellationToken);
}

public sealed record DownloadClientInfo(string Version, string? ApiVersion, string Category, bool CategoryCreated);

/// <summary>What a grab hands to the client. At least one of the three links is set.</summary>
public sealed record DownloadRequest(
    string Title,
    string? DownloadUrl,
    string? MagnetUrl,
    string? InfoHash,
    long Size,
    /// <summary>Seed criteria of the indexer, applied per torrent when the client supports it.</summary>
    double? SeedRatio = null,
    TimeSpan? SeedTime = null,
    /// <summary>The category to file it under; null takes the client's film category.</summary>
    string? Category = null,
    /// <summary>
    /// Episodes a series release covers (<c>S01E01</c>…). Real clients ignore it; the mock client writes one file per
    /// episode so a season pack arrives as a multi-file torrent.
    /// </summary>
    IReadOnlyList<string>? EpisodeLabels = null);

public enum DownloadItemStatus
{
    /// <summary>Waiting for a slot, checking, fetching metadata.</summary>
    Queued = 1,

    Downloading = 2,

    /// <summary>Stopped by the user or by the client's rules.</summary>
    Paused = 3,

    /// <summary>Running but without any peer.</summary>
    Stalled = 4,

    /// <summary>Every byte is there; the client seeds or is done seeding.</summary>
    Completed = 5,

    /// <summary>The client gave up: error state, missing files.</summary>
    Failed = 6,

    Unknown = 7,
}

/// <summary>One torrent as the client reports it, reduced to what the queue and the import need.</summary>
public sealed record DownloadItem(
    string DownloadId,
    string Title,
    long TotalSize,
    long RemainingSize,
    /// <summary>0 to 1.</summary>
    double Progress,
    DownloadItemStatus Status,
    TimeSpan? Eta,
    long DownloadRate,
    /// <summary>Where the client put the content, when known.</summary>
    string? OutputPath,
    /// <summary>The client's own words when something is off (error text, "moving"…).</summary>
    string? Message,
    double? Ratio,
    TimeSpan? SeedingTime,
    int? Seeders,
    DateTimeOffset? AddedAt,
    DateTimeOffset? CompletedAt,
    /// <summary>The client's category for it: tells a series download from a film one.</summary>
    string? Category = null);
