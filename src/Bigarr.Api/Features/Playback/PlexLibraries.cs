using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Features.Playback;

/// <summary>The folders of each Plex library, remembered for an hour: they tell which version a library holds.</summary>
public sealed class PlexLibraries(PlexClient plex, TimeProvider time, ILogger<PlexLibraries> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<int, (DateTimeOffset At, List<PlexSection> Sections)> _sections = new();

    /// <summary>The library's folders as the server sees them; empty when unknown or Plex does not answer.</summary>
    public async Task<IReadOnlyList<string>> FoldersAsync(MediaServer server, string? sectionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (sectionId is null)
        {
            return [];
        }

        var now = time.GetUtcNow();
        if (!_sections.TryGetValue(server.Id, out var known) || now - known.At > Lifetime)
        {
            try
            {
                known = (now, await plex.SectionsAsync(server, ct));
                _sections[server.Id] = known;
            }
            catch (MediaServerException ex)
            {
                logger.LogDebug("Libraries of {Server} unavailable: {Error}", server.Name, ex.Message);
                return [];
            }
        }

        return known.Sections.FirstOrDefault(s => s.Key == sectionId)?.Locations ?? [];
    }
}
