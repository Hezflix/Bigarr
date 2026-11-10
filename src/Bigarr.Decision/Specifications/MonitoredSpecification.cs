namespace Bigarr.Decision.Specifications;

/// <summary>
/// Automatic paths only: an unmonitored version never grabs. The interactive search lets the user override. For a
/// series version, Sonarr's <c>MonitoredEpisodeSpecification</c>: the version must be monitored and so must every
/// episode the release covers.
/// </summary>
public sealed class MonitoredSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Episodes is not { } episodes)
        {
            return target.Target.Monitored ? null : new Rejection("Version is not monitored");
        }

        if (!episodes.VersionMonitored)
        {
            return new Rejection("Series version is not monitored");
        }

        var unmonitored = episodes.Slots.Where(s => !s.Target.Monitored).ToList();
        if (unmonitored.Count == 0)
        {
            return null;
        }

        return episodes.Slots.Count == 1
            ? new Rejection("Episode is not monitored")
            : new Rejection($"One or more episodes is not monitored: {EpisodeCoverage.Describe(unmonitored)}");
    }
}
