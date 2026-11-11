using Bigarr.Domain.Acquisition;

namespace Bigarr.Domain.Tv;

/// <summary>
/// A release an automatic path (RSS, a search) would have taken for a series version, had it not matched its episodes
/// on the series' alternative order only, with nothing in the release to confirm it (blueprint §8, S6): it waits here for
/// the user to grab it or dismiss it. One row per (version, release); a dismissed one stays so the next pass does not
/// bring it back.
/// </summary>
public sealed class HeldRelease
{
    public int Id { get; set; }

    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    public int SeriesId { get; set; }

    /// <summary>The shared ids of the episodes the release covers (on the series' order).</summary>
    public List<int> EpisodeIds { get; set; } = [];

    /// <summary><c>S01E05</c>, <c>S01E05-E06</c> in TVDB numbers at the time it was held.</summary>
    public required string Episodes { get; set; }

    public required string Title { get; set; }

    public required string Guid { get; set; }

    public int? IndexerId { get; set; }

    public string? IndexerName { get; set; }

    public string? DownloadUrl { get; set; }

    public string? MagnetUrl { get; set; }

    public string? InfoHash { get; set; }

    public long Size { get; set; }

    public int? Seeders { get; set; }

    public IndexerFlags Flags { get; set; }

    public DownloadProtocol Protocol { get; set; } = DownloadProtocol.Torrent;

    public DateTimeOffset? PublishDate { get; set; }

    public string? Quality { get; set; }

    public int CustomFormatScore { get; set; }

    public EpisodeNumbering Numbering { get; set; } = EpisodeNumbering.Order;

    public required string Reason { get; set; }

    /// <summary><c>rss</c>, <c>search:cutoffUnmet</c>…: the path that held it.</summary>
    public required string Source { get; set; }

    public DateTime HeldAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last time a pass saw it again.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime? DismissedAt { get; set; }
}
