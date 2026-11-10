using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Shadow;

/// <summary>
/// The series twin of <see cref="ShadowPick"/>: what Bigarr would have sent to a download client for a series version
/// while it runs in shadow mode. One row per version and release however often it is picked again, with one
/// <see cref="SeriesShadowPickEpisode"/> per (version, episode) target the release covers (a season pack has many).
/// The series and the version are snapshotted (TVDB id, title, version name) so the row keeps its meaning after a
/// deletion; S4 pairs these rows with Sonarr's grabs. Like a film pick, it stands in for the files of its episodes in
/// later grab decisions, so only a real upgrade is picked again.
/// </summary>
public sealed class SeriesShadowPick
{
    public int Id { get; set; }

    public int? VersionId { get; set; }

    public SeriesVersion? Version { get; set; }

    public int? SeriesId { get; set; }

    public int? TvdbId { get; set; }

    public required string SeriesTitle { get; set; }

    public required string VersionName { get; set; }

    /// <summary>Who picked it first: <c>rss</c>, <c>interactive</c>…</summary>
    public required string Trigger { get; set; }

    public DateTime PickedAt { get; set; }

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

    public string? Quality { get; set; }

    public int? QualityId { get; set; }

    public int? RevisionVersion { get; set; }

    public int? RevisionReal { get; set; }

    public bool IsRepack { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<string> Languages { get; set; } = [];

    public List<string> CustomFormats { get; set; } = [];

    public int CustomFormatScore { get; set; }

    /// <summary>A season (or multi-season) pack.</summary>
    public bool FullSeason { get; set; }

    public ICollection<SeriesShadowPickEpisode> Episodes { get; set; } = [];
}

/// <summary>One episode a series pick covers: its target while it exists, its numbers for good.</summary>
public sealed class SeriesShadowPickEpisode
{
    public int Id { get; set; }

    public int PickId { get; set; }

    public SeriesShadowPick Pick { get; set; } = null!;

    /// <summary>The (version, episode) target; null once the target is deleted.</summary>
    public int? TargetId { get; set; }

    /// <summary>The shared episode; null once the episode is deleted.</summary>
    public int? EpisodeId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public int? AbsoluteEpisodeNumber { get; set; }
}
