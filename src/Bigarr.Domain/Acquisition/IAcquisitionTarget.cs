namespace Bigarr.Domain.Acquisition;

/// <summary>What the shared kernel (Decision, Import, Scheduler) is allowed to know about a target.</summary>
public interface IAcquisitionTarget
{
    int Id { get; }

    TargetKind Kind { get; }

    bool Monitored { get; }

    int QualityProfileId { get; }

    AcquisitionConstraints? Constraints { get; }

    /// <summary><c>false</c> for one-shot versions: grab once, never upgrade.</summary>
    bool UpgradesAllowed { get; }
}
