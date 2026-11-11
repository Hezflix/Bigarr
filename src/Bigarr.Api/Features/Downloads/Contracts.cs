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
    int Priority = 1,
    /// <summary>Category of series grabs (Sonarr's <c>tv-sonarr</c>). Null keeps the current one (<c>bigarr-tv</c> for a new client).</summary>
    string? SeriesCategory = null);

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
    DateTime CreatedAt,
    string SeriesCategory = DownloadClient.DefaultSeriesCategory);

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
    string? Indexer = null,
    /// <summary>When the indexer published it; shadow mode keeps it with the pick.</summary>
    DateTimeOffset? PublishDate = null,
    /// <summary>
    /// A series grab: the shared ids of the episodes the release covers, and <see cref="VersionId"/> is then the series
    /// version. One <c>GrabTarget</c> per (version, episode) pair.
    /// </summary>
    List<int>? EpisodeIds = null,
    /// <summary>A series grab: which numbering the release matched its episodes on (the search returns it).</summary>
    Bigarr.Domain.Tv.EpisodeNumbering? Numbering = null)
{
    public bool IsSeries => EpisodeIds is { Count: > 0 };

    public static GrabRequest ForEpisodes(ReleaseInfo release, int seriesVersionId, IEnumerable<int> episodeIds, Bigarr.Domain.Tv.EpisodeNumbering? numbering = null) =>
        From(release, seriesVersionId) with { EpisodeIds = episodeIds.Distinct().ToList(), Numbering = numbering };

    /// <summary>What the automatic paths (RSS, searches) send: the release as the indexer returned it.</summary>
    public static GrabRequest From(ReleaseInfo release, int versionId)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new GrabRequest(versionId, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash, release.Size, release.Seeders, release.Flags, release.Protocol, release.IndexerName, release.PublishDate);
    }
}

/// <summary>
/// A grab sent to a download client, or, in shadow mode, the pick recorded in its place: then only
/// <see cref="ShadowPickId"/> and <see cref="GrabbedAt"/> are set.
/// </summary>
public sealed record GrabResponse(int? GrabId, string? DownloadId, int? DownloadClientId, string? DownloadClient, GrabState? State, DateTime GrabbedAt, int? ShadowPickId = null)
{
    public bool Shadow => ShadowPickId is not null;
}

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
    bool Tracked)
{
    /// <summary>A series download: a series grab, or a torrent of the series category.</summary>
    public bool Series { get; init; }

    public int? SeriesId { get; init; }

    public string? SeriesTitle { get; init; }

    public int? SeriesYear { get; init; }

    /// <summary>The episodes the grab covers, in order; empty for a film or an untracked torrent.</summary>
    public List<QueueEpisodeResponse> Episodes { get; init; } = [];
}

/// <param name="HasFile">The version already has a file for it (an import in progress, or an upgrade).</param>
public sealed record QueueEpisodeResponse(int EpisodeId, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, string Title, bool HasFile);

public sealed record QueueClientStatusResponse(int DownloadClientId, string Name, bool Healthy, string? LastError, DateTime? LastSuccessAt);

public sealed record QueueResponse(
    DateTime? LastPollAt,
    DateTime? NextPollAt,
    int PollSeconds,
    /// <summary>Increments on every publication; lets a client tell a real change from a reconnect.</summary>
    long Revision,
    List<QueueItemResponse> Items,
    List<QueueClientStatusResponse> Clients);
