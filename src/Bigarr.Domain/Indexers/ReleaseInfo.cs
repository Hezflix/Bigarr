using Bigarr.Domain.Acquisition;

namespace Bigarr.Domain.Indexers;

/// <summary>One release as returned by an indexer, before parsing and before any decision.</summary>
public sealed class ReleaseInfo
{
    /// <summary>Stable identifier on the indexer side; what the blocklist and the history remember.</summary>
    public required string Guid { get; set; }

    public required string Title { get; set; }

    public int IndexerId { get; set; }

    public required string IndexerName { get; set; }

    public int IndexerPriority { get; set; }

    public DownloadProtocol Protocol { get; set; } = DownloadProtocol.Torrent;

    public long Size { get; set; }

    public string? DownloadUrl { get; set; }

    public string? MagnetUrl { get; set; }

    public string? InfoUrl { get; set; }

    public string? InfoHash { get; set; }

    public int? Seeders { get; set; }

    public int? Leechers { get; set; }

    public DateTimeOffset PublishDate { get; set; }

    public List<int> Categories { get; set; } = [];

    /// <summary>Identifiers the indexer attached to the release, when it did; they short-circuit title matching.</summary>
    public string? ImdbId { get; set; }

    public int? TmdbId { get; set; }

    public IndexerFlags Flags { get; set; }

    public double? MinimumRatio { get; set; }

    public TimeSpan? MinimumSeedTime { get; set; }
}
