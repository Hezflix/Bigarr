using System.Net;
using System.Text.Json;
using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>
/// Plex Media Server over its HTTP API with a token: sections from <c>/library/sections</c>, partial scan through
/// <c>/library/sections/{key}/refresh?path=</c> like Radarr's Plex connection does, full scan without the path.
/// </summary>
public sealed class PlexClient(IHttpClientFactory httpClientFactory) : IMediaServerClient
{
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

    private async Task<List<Section>> SectionsAsync(MediaServer server, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(server, "/library/sections", ct);
        var sections = new List<Section>();
        if (!doc.RootElement.TryGetProperty("MediaContainer", out var container) || !container.TryGetProperty("Directory", out var directories))
        {
            return sections;
        }

        foreach (var directory in directories.EnumerateArray())
        {
            var key = directory.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var type = directory.TryGetProperty("type", out var t) ? t.GetString() : null;
            var locations = new List<string>();
            if (directory.TryGetProperty("Location", out var locationArray))
            {
                foreach (var location in locationArray.EnumerateArray())
                {
                    if (location.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } locationPath)
                    {
                        locations.Add(locationPath);
                    }
                }
            }

            sections.Add(new Section(key, string.Equals(type, "movie", StringComparison.OrdinalIgnoreCase), locations));
        }

        return sections;
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

    private async Task<HttpResponseMessage> SendAsync(MediaServer server, HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, server.BaseUrl + path);
        request.Headers.TryAddWithoutValidation("X-Plex-Token", server.Token.Trim());
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "bigarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Bigarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", Features.Server.SystemEndpoints.Version);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(MediaServersExtensions.HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
            throw new MediaServerException("Plex refused the token (HTTP 401).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new MediaServerException($"Plex answered HTTP {status} on {path}.");
        }

        return response;
    }

    private sealed record Section(string Key, bool IsMovies, List<string> Locations);
}
