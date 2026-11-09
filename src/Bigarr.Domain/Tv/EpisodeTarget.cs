using Bigarr.Domain.Acquisition;

namespace Bigarr.Domain.Tv;

/// <summary>
/// One episode wanted by one series version: what the queue, the history, the blocklist and the decision engine point
/// at for series (TPT child of <see cref="AcquisitionTarget"/>, kind <see cref="TargetKind.Episode"/>). A season pack is
/// one grab with one <c>GrabTarget</c> per pair. The profile and constraints are the version's: the interface resolves
/// them, nothing is copied.
/// </summary>
public sealed class EpisodeTarget : AcquisitionTarget, IAcquisitionTarget
{
    public EpisodeTarget()
        : base(TargetKind.Episode)
    {
    }

    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    public int EpisodeId { get; set; }

    public Episode Episode { get; set; } = null!;

    /// <summary>The file holding this episode for this version; several targets share one multi-episode file.</summary>
    public int? EpisodeFileId { get; set; }

    public EpisodeFile? EpisodeFile { get; set; }

    public bool HasFile => EpisodeFileId is not null;

    /// <summary>Resolved from the version (load it with the target).</summary>
    public int QualityProfileId => Version.QualityProfileId;

    public AcquisitionConstraints? Constraints => Version.Constraints;

    public bool UpgradesAllowed => Version.UpgradesAllowed;
}
