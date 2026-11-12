namespace Bigarr.Decision.Specifications;

/// <summary>
/// Never two active downloads for one target. For a series version, every episode the release covers
/// counts: a pack waits while one of its episodes is downloading. Comparing against the queued release comes with M4.
/// </summary>
public sealed class QueueSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Episodes is { } episodes)
        {
            var busy = episodes.Slots.Where(s => s.HasActiveGrab).ToList();
            return busy.Count == 0
                ? null
                : new Rejection($"A download is already active for {EpisodeCoverage.Describe(busy)}", RejectionType.Temporary);
        }

        return target.HasActiveGrab ? new Rejection("A download is already active for this version", RejectionType.Temporary) : null;
    }
}
