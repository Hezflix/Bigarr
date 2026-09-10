namespace Bigarr.Decision;

/// <summary>
/// A release is grabbed for one target only (blueprint §3.3): among the targets that accept it, the one whose
/// satisfied constraints are the most specific wins, then the default target, then the oldest.
/// </summary>
public static class VersionAttribution
{
    public static ReleaseDecision? Choose(IEnumerable<ReleaseDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        return decisions
            .Where(d => d.Approved)
            .OrderByDescending(d => d.Target.Target.Constraints?.Specificity ?? 0)
            .ThenByDescending(d => d.Target.IsDefaultTarget)
            .ThenBy(d => d.Target.Target.Id)
            .FirstOrDefault();
    }
}
