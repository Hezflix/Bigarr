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

    /// <summary>
    /// Where an interactive grab goes unless the user picks another target: the attributed one when a target accepts
    /// the release, otherwise the one closest to accepting it (fewest permanent rejections, then fewest rejections),
    /// then the default target, then the oldest. A target busy with a download still counts: the release fits it, and
    /// the user sees why the grab has to wait. Null only when there is no decision at all.
    /// </summary>
    public static ReleaseDecision? Suggest(IReadOnlyCollection<ReleaseDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        return Choose(decisions) ?? decisions
            .OrderBy(d => d.Rejections.Count(r => r.Type == RejectionType.Permanent))
            .ThenBy(d => d.Rejections.Count)
            .ThenByDescending(d => d.Target.IsDefaultTarget)
            .ThenBy(d => d.Target.Target.Id)
            .FirstOrDefault();
    }
}
