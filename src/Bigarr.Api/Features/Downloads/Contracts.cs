using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Downloads;

/// <summary>Shared by create and update: a client is small enough to be sent whole.</summary>
public sealed record DownloadClientRequest(
    string Name,
    DownloadClientImplementation Implementation,
    bool Enabled = true,
    string? Host = null,
    int Port = 8080,
    bool UseSsl = false,
    string? UrlBase = null,
    string? Username = null,
    string? Password = null,
    string Category = DownloadClient.DefaultCategory,
    bool AddPaused = false,
    int Priority = 1);

public sealed record DownloadClientResponse(
    int Id,
    string Name,
    DownloadClientImplementation Implementation,
    bool Enabled,
    string? Host,
    int Port,
    bool UseSsl,
    string? UrlBase,
    string? Username,
    string? Password,
    string Category,
    bool AddPaused,
    int Priority,
    DownloadProtocol Protocol,
    DateTime CreatedAt);

public sealed record DownloadClientTestResponse(string Version, string? ApiVersion, string Category, bool CategoryCreated);

/// <summary>
/// The release the user picked, sent back as the search returned it: the search keeps no state, so the client
/// carries the links. The title is parsed again here for the history.
/// </summary>
public sealed record GrabRequest(
    int VersionId,
    int IndexerId,
    string Guid,
    string Title,
    string? DownloadUrl = null,
    string? MagnetUrl = null,
    string? InfoHash = null,
    long Size = 0,
    int? Seeders = null,
    IndexerFlags Flags = IndexerFlags.None,
    DownloadProtocol Protocol = DownloadProtocol.Torrent,
    /// <summary>Indexer name as displayed, kept when the indexer row is gone.</summary>
    string? Indexer = null)
{
    /// <summary>What the automatic paths (RSS, searches) send: the release as the indexer returned it.</summary>
    public static GrabRequest From(ReleaseInfo release, int versionId)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new GrabRequest(versionId, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash, release.Size, release.Seeders, release.Flags, release.Protocol, release.IndexerName);
    }
}

public sealed record GrabResponse(int GrabId, string DownloadId, int DownloadClientId, string DownloadClient, GrabState State, DateTime GrabbedAt);

/// <summary>One line of the queue: a tracked grab joined to what its client reports, or an untracked torrent of our category.</summary>
public sealed record QueueItemResponse(
    int? GrabId,
    string DownloadId,
    string Title,
    string? Quality,
    int? MovieId,
    string? MovieTitle,
    int? MovieYear,
    int? VersionId,
    string? VersionName,
    GrabState? State,
    DownloadItemStatus Status,
    long Size,
    long SizeLeft,
    /// <summary>0 to 1.</summary>
    double Progress,
    long DownloadRate,
    int? EtaSeconds,
    int DownloadClientId,
    string DownloadClient,
    int? IndexerId,
    string? Indexer,
    string? OutputPath,
    List<StatusMessage> StatusMessages,
    DateTime AddedAt,
    /// <summary>False for a torrent found in our category that Bigarr never grabbed.</summary>
    bool Tracked);

public sealed record QueueClientStatusResponse(int DownloadClientId, string Name, bool Healthy, string? LastError, DateTime? LastSuccessAt);

public sealed record QueueResponse(
    DateTime? LastPollAt,
    DateTime? NextPollAt,
    int PollSeconds,
    /// <summary>Increments on every publication; lets a client tell a real change from a reconnect.</summary>
    long Revision,
    List<QueueItemResponse> Items,
    List<QueueClientStatusResponse> Clients);
