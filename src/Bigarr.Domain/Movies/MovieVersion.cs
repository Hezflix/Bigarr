using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Movies;

/// <summary>
/// "I want this film in this flavour." The unit of monitoring, search and upgrade.
/// Inherits the <see cref="AcquisitionTarget"/> base table (TPT).
/// </summary>
public sealed class MovieVersion : AcquisitionTarget, IAcquisitionTarget
{
    public MovieVersion()
        : base(TargetKind.MovieVersion)
    {
    }

    public int MovieId { get; set; }

    public Movie Movie { get; set; } = null!;

    /// <summary>Short slug, unique per movie: <c>1080p</c>, <c>4k</c>, <c>directors-cut</c>.</summary>
    public required string Name { get; set; }

    public VersionMode Mode { get; set; } = VersionMode.Managed;

    /// <summary>An attribute of the version, not its key. One-shot versions have one too (acceptability + compat).</summary>
    public int QualityProfileId { get; set; }

    public QualityProfile QualityProfile { get; set; } = null!;

    public AcquisitionConstraints? Constraints { get; set; }

    /// <summary>Exactly one per movie, always a managed version. Enforced by a partial unique index.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Resolved from the movie's default at creation, then frozen. Moving is an explicit command.</summary>
    public int RootFolderId { get; set; }

    public RootFolder RootFolder { get; set; } = null!;

    /// <summary>Computed from the naming template at creation, observed on disk by the library scan, then frozen.</summary>
    public required string FolderName { get; set; }

    public MinimumAvailability MinimumAvailability { get; set; } = MinimumAvailability.Released;

    public MediaFile? File { get; set; }

    /// <summary>Added to the movie's tags; routes a version to a specific indexer or notification.</summary>
    public ICollection<Tag> Tags { get; set; } = [];

    public bool UpgradesAllowed => Mode == VersionMode.Managed;
}
