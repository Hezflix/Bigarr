namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// What the follow remembers of each Radarr between two passes (see <see cref="FollowMemory{TEntry}"/>): when it last read
/// the whole film list and how many films each profile held then.
/// </summary>
public sealed class RadarrFollowMemory : FollowMemory<RadarrFollowMemory.Entry>
{
    public sealed record Entry(DateTime ListReadAt, Dictionary<int, int> FilmsPerProfile);
}
