using System.Collections.Concurrent;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// What the follow remembers of each Radarr between two passes, in memory only: when it last read the whole film list and
/// how many films each profile held then. After a restart, or a pass that failed, nothing is remembered and the next pass
/// reads the whole list, so nothing is missed.
/// </summary>
public sealed class RadarrFollowMemory
{
    public sealed record Entry(DateTime ListReadAt, Dictionary<int, int> FilmsPerProfile);

    private readonly ConcurrentDictionary<int, Entry> _entries = new();

    public Entry? Get(int followedRadarrId) => _entries.GetValueOrDefault(followedRadarrId);

    public void Remember(int followedRadarrId, Entry entry) => _entries[followedRadarrId] = entry;

    public void Forget(int followedRadarrId) => _entries.TryRemove(followedRadarrId, out _);
}
