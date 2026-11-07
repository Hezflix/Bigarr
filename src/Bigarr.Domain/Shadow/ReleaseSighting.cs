namespace Bigarr.Domain.Shadow;

/// <summary>
/// A release the RSS pass saw in shadow mode, with what the library made of it, kept with no expiry so the comparison can
/// say whether Bigarr ever saw a release Radarr grabbed, and what it thought of it then. One row per indexer and guid.
/// </summary>
public sealed class ReleaseSighting
{
    public long Id { get; set; }

    public int? IndexerId { get; set; }

    public required string IndexerName { get; set; }

    public required string Guid { get; set; }

    public string? InfoHash { get; set; }

    public required string Title { get; set; }

    public DateTime SeenAt { get; set; }

    public DateTimeOffset? PublishDate { get; set; }

    /// <summary>The film of the library it was matched to; null with <see cref="UnmatchedReason"/>.</summary>
    public int? TmdbId { get; set; }

    public string? UnmatchedReason { get; set; }

    public string? AttributedVersion { get; set; }

    public List<SightingVerdict> Verdicts { get; set; } = [];
}
