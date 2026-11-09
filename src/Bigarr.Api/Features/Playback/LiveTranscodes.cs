using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.MediaServers;

namespace Bigarr.Api.Features.Playback;

/// <summary>
/// The latest transcode figures of each live stream, in memory: they only mean something while the stream plays, so
/// they are neither stored nor kept in the play. Refreshed whenever the list of streams is read.
/// </summary>
public sealed class LiveTranscodes
{
    private readonly ConcurrentDictionary<(int ServerId, string SessionKey), PlexTranscode> _transcodes = new();

    public void Set(int serverId, string sessionKey, PlexTranscode? transcode)
    {
        if (transcode is null)
        {
            _transcodes.TryRemove((serverId, sessionKey), out _);
        }
        else
        {
            _transcodes[(serverId, sessionKey)] = transcode;
        }
    }

    public PlexTranscode? Get(int serverId, string sessionKey) => _transcodes.TryGetValue((serverId, sessionKey), out var transcode) ? transcode : null;
}
