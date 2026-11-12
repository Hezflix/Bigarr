using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Tv;

/// <summary>
/// "I want this show in this flavour", the series twin of <see cref="MovieVersion"/>. Not an acquisition target itself:
/// a series is wanted episode by episode, so the unit of monitoring, search, queue and history is the
/// (version, episode) pair, <see cref="EpisodeTarget"/>. The version carries what the pairs share: profile, constraints,
/// folder, tags.
/// </summary>
public sealed class SeriesVersion
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    /// <summary>Short slug, unique per series: <c>1080p</c>, <c>4k</c>, <c>vostfr</c>.</summary>
    public required string Name { get; set; }

    public VersionMode Mode { get; set; } = VersionMode.Managed;

    public int QualityProfileId { get; set; }

    public QualityProfile QualityProfile { get; set; } = null!;

    public AcquisitionConstraints? Constraints { get; set; }

    /// <summary>Exactly one per series, always a managed version. Enforced by a partial unique index.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Series-level switch of the version (Sonarr's series <c>monitored</c>); episodes keep their own flags.</summary>
    public bool Monitored { get; set; } = true;

    /// <summary>Episodes sorted into season folders (<c>Season 1</c> by default), as in Sonarr.</summary>
    public bool SeasonFolder { get; set; } = true;

    public NewItemMonitoring MonitorNewItems { get; set; } = NewItemMonitoring.All;

    /// <summary>Resolved from the series' default at creation, then frozen. Moving is an explicit command.</summary>
    public int RootFolderId { get; set; }

    public RootFolder RootFolder { get; set; } = null!;

    /// <summary>Computed from the template at creation, observed on disk by the scan, then frozen.</summary>
    public required string FolderName { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public ICollection<EpisodeTarget> Episodes { get; set; } = [];

    public ICollection<SeriesVersionSeason> Seasons { get; set; } = [];

    public ICollection<EpisodeFile> Files { get; set; } = [];

    /// <summary>Added to the series' tags; routes a version to a specific indexer or notification.</summary>
    public ICollection<Tag> Tags { get; set; } = [];

    public bool UpgradesAllowed => Mode == VersionMode.Managed;
}
