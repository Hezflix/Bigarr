using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>
/// Plex Media Server over its HTTP API with a token: sections from <c>/library/sections</c>, partial scan through
/// <c>/library/sections/{key}/refresh?path=</c> like Radarr's Plex connection does, full scan without the path.
/// </summary>
public sealed class PlexClient(IHttpClientFactory httpClientFactory) : IMediaServerClient
{
    public static readonly TimeSpan EventsIdleTimeout = TimeSpan.FromSeconds(60);

    private static readonly char[] Separators = ['/', '\\'];

    public bool Handles(MediaServerKind kind) => kind == MediaServerKind.Plex;

    public async Task<MediaServerInfo> TestAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var root = await GetJsonAsync(server, "/", ct);
        var container = root.RootElement.GetProperty("MediaContainer");
        var sections = await SectionsAsync(server, ct);
        return new MediaServerInfo(
            container.TryGetProperty("friendlyName", out var name) ? name.GetString() : null,
            container.TryGetProperty("version", out var version) ? version.GetString() : null,
            sections.Count(s => s.IsMovies));
    }

    public async Task NotifyAsync(MediaServer server, string path, MediaChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(path);

        var sections = (await SectionsAsync(server, ct)).Where(s => s.IsMovies).ToList();
        if (sections.Count == 0)
        {
            throw new MediaServerException("Plex has no movie library.");
        }

        // The section whose folder contains the path scans just that path; when none does, every movie section is scanned.
        var matching = sections.Where(s => s.Locations.Any(l => IsUnder(path, l))).ToList();
        foreach (var section in matching.Count > 0 ? matching : sections)
        {
            var query = matching.Count > 0 ? "?path=" + Uri.EscapeDataString(path) : string.Empty;
            using var response = await SendAsync(server, HttpMethod.Get, $"/library/sections/{section.Key}/refresh{query}", ct);
        }
    }

    public async Task RefreshAllAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        foreach (var section in (await SectionsAsync(server, ct)).Where(s => s.IsMovies))
        {
            using var response = await SendAsync(server, HttpMethod.Get, $"/library/sections/{section.Key}/refresh", ct);
        }
    }

    public static bool IsUnder(string path, string folder)
    {
        var root = folder.TrimEnd(Separators);
        return root.Length > 0
            && path.StartsWith(root, StringComparison.Ordinal)
            && (path.Length == root.Length || Array.IndexOf(Separators, path[root.Length]) >= 0);
    }

    public async Task<List<PlexSection>> SectionsAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var doc = await GetJsonAsync(server, "/library/sections", ct);
        return PlexPlayback.ParseSections(doc.RootElement);
    }

    /// <summary>Every stream playing now, all users (the token must be the server owner's).</summary>
    public async Task<List<PlexSession>> SessionsAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var doc = await GetJsonAsync(server, "/status/sessions", ct);
        return PlexPlayback.ParseSessions(doc.RootElement);
    }

    /// <summary>An item's metadata with its external ids, files and credits marker; null when Plex no longer has it.</summary>
    public async Task<PlexItem?> ItemAsync(MediaServer server, string ratingKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        try
        {
            using var doc = await GetJsonAsync(server, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}?includeMarkers=1", ct);
            return PlexPlayback.ParseItem(doc.RootElement);
        }
        catch (MediaServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>The guid, external ids and title of a show (an episode's grandparent); null when Plex no longer has it.</summary>
    public async Task<(string? Guid, PlexIds Ids, string? Title)?> IdsAsync(MediaServer server, string ratingKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        try
        {
            using var doc = await GetJsonAsync(server, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}", ct);
            return PlexPlayback.ParseIds(doc.RootElement);
        }
        catch (MediaServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Stops a stream on its player, showing <paramref name="reason"/> to the viewer. The one call that changes something on
    /// Plex during playback: made only when someone asks for it. Needs Plex Pass and the owner's token.
    /// </summary>
    public async Task TerminateAsync(MediaServer server, string sessionId, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var query = "?sessionId=" + Uri.EscapeDataString(sessionId);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            query += "&reason=" + Uri.EscapeDataString(reason.Trim());
        }

        try
        {
            using var response = await SendAsync(server, HttpMethod.Get, "/status/sessions/terminate" + query, ct);
        }
        catch (MediaServerException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new MediaServerException("Plex refused to stop the stream: it takes Plex Pass and the server owner's token.", ex) { StatusCode = ex.StatusCode };
        }
    }

    /// <summary>
    /// The server's <c>playing</c> notifications as server-sent events, until the connection drops. Plex pings every
    /// ~10 s (passed on as <see cref="PlexPlayingEvent.Ping"/>); a silent minute is taken as a dead connection.
    /// </summary>
    public async IAsyncEnumerable<PlexPlayingEvent> PlayingEventsAsync(MediaServer server, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var response = await SendAsync(server, HttpMethod.Get, "/:/eventsource/notifications?filters=playing", ct, MediaServersExtensions.EventsHttpClientName, "text/event-stream");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(body);
        string? eventName = null;
        while (true)
        {
            string? line;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                idle.CancelAfter(EventsIdleTimeout);
                try
                {
                    line = await reader.ReadLineAsync(idle.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new MediaServerException($"Plex sent nothing for {EventsIdleTimeout.TotalSeconds:0} s.");
                }
                catch (IOException ex)
                {
                    throw new MediaServerException("Plex closed the event stream.", ex);
                }
            }

            if (line is null)
            {
                throw new MediaServerException("Plex closed the event stream.");
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal) && eventName == "ping")
            {
                yield return PlexPlayingEvent.Ping;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal) && eventName == "playing" && PlexPlayback.ParsePlayingEvent(line[5..].Trim()) is { } playing)
            {
                yield return playing;
            }
            else if (line.Length == 0)
            {
                eventName = null;
            }
        }
    }

    private async Task<JsonDocument> GetJsonAsync(MediaServer server, string path, CancellationToken ct)
    {
        using var response = await SendAsync(server, HttpMethod.Get, path, ct);
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new MediaServerException($"Plex answered something that is not JSON on {path}: is the URL a Plex server?", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(MediaServer server, HttpMethod method, string path, CancellationToken ct, string clientName = MediaServersExtensions.HttpClientName, string accept = "application/json")
    {
        using var request = new HttpRequestMessage(method, server.BaseUrl + path);
        request.Headers.TryAddWithoutValidation("X-Plex-Token", server.Token.Trim());
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "bigarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Bigarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", Features.Server.SystemEndpoints.Version);
        request.Headers.Accept.ParseAdd(accept);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(clientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            throw new MediaServerException(ex is TaskCanceledException ? "Plex did not answer before the timeout." : $"Plex could not be reached: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new MediaServerException("Plex refused the token (HTTP 401).") { StatusCode = HttpStatusCode.Unauthorized };
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new MediaServerException($"Plex answered HTTP {status} on {path}.") { StatusCode = (HttpStatusCode)status };
        }

        return response;
    }

}
