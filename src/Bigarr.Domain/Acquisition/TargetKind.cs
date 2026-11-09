namespace Bigarr.Domain.Acquisition;

/// <summary>Concrete type behind an <see cref="AcquisitionTarget"/> row.</summary>
public enum TargetKind
{
    MovieVersion = 1,

    /// <summary>One episode wanted by one series version: the <c>EpisodeTarget</c> pair (blueprint §9, S1).</summary>
    Episode = 2,
}
