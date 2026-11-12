namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// What the follow remembers of each Sonarr between two passes (see <see cref="FollowMemory{TEntry}"/>): when it last read
/// the whole series list and how many series each profile held then, and when it last read the episodes of every series
/// (a day's cadence; a restart starts a new day rather than read thousands of series at once).
/// </summary>
public sealed class SonarrFollowMemory : FollowMemory<SonarrFollowMemory.Entry>
{
    public sealed record Entry(DateTime ListReadAt, Dictionary<int, int> SeriesPerProfile, DateTime EverySeriesReadAt);

    /// <summary>The series list is to be read whole again; when the episodes of every series were last read is kept.</summary>
    public void ForgetList(int followedSonarrId)
    {
        if (Get(followedSonarrId) is { } entry)
        {
            Remember(followedSonarrId, entry with { ListReadAt = DateTime.MinValue });
        }
    }
}
