using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Shadow;

/// <summary>
/// What Bigarr would have sent to a download client while it runs in shadow mode next to Radarr: one row per version
/// and release, however often the release is picked again. A pick changes nothing about the version: the version only
/// moves when the Radarr it follows grabs and imports. The film and version are snapshotted (TMDB id, title, version
/// name) so the row keeps its meaning, and its pairing with Radarr's grabs, after a deletion.
/// </summary>
public sealed class ShadowPick
{
    public int Id { get; set; }

    public int? VersionId { get; set; }

    public MovieVersion? Version { get; set; }

    public int? MovieId { get; set; }

    public int TmdbId { get; set; }

    public required string MovieTitle { get; set; }

    public required string VersionName { get; set; }

    /// <summary>Who picked it first: <c>rss</c>, <c>search:user</c>, <c>search:cutoffUnmet</c>, <c>interactive</c>…</summary>
    public required string Trigger { get; set; }

    public DateTime PickedAt { get; set; }

    /// <summary>The last time a pass picked the same release again for the same version.</summary>
    public DateTime LastPickedAt { get; set; }

    public int Picks { get; set; } = 1;

    public required string ReleaseTitle { get; set; }

    public required string ReleaseGuid { get; set; }

    public string? InfoHash { get; set; }

    public int? IndexerId { get; set; }

    public string? IndexerName { get; set; }

    public long Size { get; set; }

    public int? Seeders { get; set; }

    public IndexerFlags IndexerFlags { get; set; }

    public DateTimeOffset? PublishDate { get; set; }

    /// <summary>What the parser reads in the title; null when it reads no quality.</summary>
    public string? Quality { get; set; }

    public int? QualityId { get; set; }

    public int? RevisionVersion { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<string> Languages { get; set; } = [];

    public List<string> CustomFormats { get; set; } = [];

    /// <summary>Scored against the version's profile when the pick was made.</summary>
    public int CustomFormatScore { get; set; }

    /// <summary>What the Radarr following the version made of this release, read from its <c>/parse</c> after the pick.</summary>
    public RadarrView? RadarrView { get; set; }

    public DateTime? RadarrViewAt { get; set; }
}
