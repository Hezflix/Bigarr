namespace Bigarr.Decision.Specifications;

/// <summary>Never two active downloads for one target (blueprint §3.5). Comparing against the queued release comes with M4.</summary>
public sealed class QueueSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        target.HasActiveGrab ? new Rejection("A download is already active for this version", RejectionType.Temporary) : null;
}
