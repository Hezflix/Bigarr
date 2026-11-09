using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Infrastructure.MediaServers;

public enum MediaChange
{
    Created = 1,
    Modified = 2,
    Deleted = 3,
}

public sealed record MediaServerInfo(string? Name, string? Version, int MovieLibraries);

/// <summary>Raised when a media server cannot be reached or refuses the token; the message is meant for the screen.</summary>
public sealed class MediaServerException(string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>The HTTP status the server answered, when it answered one.</summary>
    public System.Net.HttpStatusCode? StatusCode { get; init; }
}

/// <summary>What Bigarr asks of a media server: prove the connection, scan one folder, scan everything.</summary>
public interface IMediaServerClient
{
    bool Handles(MediaServerKind kind);

    Task<MediaServerInfo> TestAsync(MediaServer server, CancellationToken ct);

    /// <summary>Scans one folder, already translated to the server's own paths.</summary>
    Task NotifyAsync(MediaServer server, string path, MediaChange change, CancellationToken ct);

    Task RefreshAllAsync(MediaServer server, CancellationToken ct);
}

/// <summary>Picks the client for a server's kind.</summary>
public sealed class MediaServerClients(IEnumerable<IMediaServerClient> clients)
{
    private readonly List<IMediaServerClient> _clients = [.. clients];

    public IMediaServerClient For(MediaServerKind kind) =>
        _clients.FirstOrDefault(c => c.Handles(kind)) ?? throw new MediaServerException($"No client for {kind}.");
}
