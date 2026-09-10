namespace Bigarr.Decision.Specifications;

/// <summary>Automatic paths only: an unmonitored version never grabs. The interactive search lets the user override.</summary>
public sealed class MonitoredSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        target.Target.Monitored ? null : new Rejection("Version is not monitored");
}
