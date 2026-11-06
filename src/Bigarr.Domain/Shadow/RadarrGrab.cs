namespace Bigarr.Domain.Shadow;

public enum RadarrGrabState
{
    /// <summary>Radarr sent it to its client; no import or failure read yet.</summary>
    Grabbed = 1,

    Imported = 2,

    Failed = 3,

    /// <summary>Radarr's <c>downloadIgnored</c>: the user told Radarr to forget the download.</summary>
    Ignored = 4,
}

/// <summary>
/// A release a followed Radarr grabbed, read from its history: the other side of the comparison with Bigarr's
/// <see cref="ShadowPick"/>. Keyed on Radarr's history id, so reading the same lines again adds nothing. The film is
/// snapshotted (TMDB id, title); <see cref="MovieId"/> and <see cref="VersionId"/> are filled once the film is in Bigarr.
/// </summary>
public sealed class RadarrGrab
{
    public int Id { get; set; }

    public int FollowedRadarrId { get; set; }

    public FollowedRadarr FollowedRadarr { get; set; } = null!;

    public int RadarrHistoryId { get; set; }

    public int RadarrMovieId { get; set; }

    public int TmdbId { get; set; }

    public int? MovieId { get; set; }

    public int? VersionId { get; set; }

    public required string MovieTitle { get; set; }

    public required string VersionName { get; set; }

    public DateTime GrabbedAt { get; set; }

    public required string ReleaseTitle { get; set; }

    public string? ReleaseGuid { get; set; }

    /// <summary>Radarr's <c>torrentInfoHash</c>, upper case like qBittorrent's.</summary>
    public string? InfoHash { get; set; }

    /// <summary>The download client's id (the info hash for qBittorrent): later import and failure lines carry it.</summary>
    public string? DownloadId { get; set; }

    public string? IndexerName { get; set; }

    public long Size { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>Radarr's <c>releaseSource</c>: <c>Rss</c>, <c>Search</c>, <c>UserInvokedSearch</c>, <c>InteractiveSearch</c>, <c>ReleasePush</c>.</summary>
    public string? ReleaseSource { get; set; }

    public string? DownloadClient { get; set; }

    public string? Quality { get; set; }

    public int? QualityId { get; set; }

    public int? RevisionVersion { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<string> Languages { get; set; } = [];

    public List<string> CustomFormats { get; set; } = [];

    /// <summary>Radarr's own score of the release when it grabbed it.</summary>
    public int? CustomFormatScore { get; set; }

    public RadarrGrabState State { get; set; } = RadarrGrabState.Grabbed;

    public DateTime? ResolvedAt { get; set; }

    /// <summary>Bigarr's engine on the same release for the same version, when the grab was read.</summary>
    public BigarrVerdict? BigarrVerdict { get; set; }
}
