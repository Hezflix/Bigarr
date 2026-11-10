using System.Collections.Concurrent;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// What the follow remembers of each Sonarr between two passes, in memory only: when it last read the whole series list and
/// how many series each profile held then, and when it last read the episodes of every series (a day's
/// cadence; a restart starts a new day rather than read thousands of series at once). After a restart, or a pass that
/// failed, nothing is remembered and the next pass reads the whole series list, so nothing is missed.
/// </summary>
public sealed class SonarrFollowMemory
{
    public sealed record Entry(DateTime ListReadAt, Dictionary<int, int> SeriesPerProfile, DateTime EverySeriesReadAt);

    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    public Entry? Get(int followedSonarrId) => _entries.GetValueOrDefault(followedSonarrId);

    public void Remember(int followedSonarrId, Entry entry) => _entries[followedSonarrId] = entry;

    public void Forget(int followedSonarrId) => _entries.TryRemove(followedSonarrId, out _);
}
