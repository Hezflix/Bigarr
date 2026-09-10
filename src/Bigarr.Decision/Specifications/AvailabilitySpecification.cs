namespace Bigarr.Decision.Specifications;

/// <summary>Automatic paths only: nothing is grabbed before the version's minimum availability (blueprint §3.3).</summary>
public sealed class AvailabilitySpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        target.IsAvailable ? null : new Rejection("Minimum availability not reached yet", RejectionType.Temporary);
}
