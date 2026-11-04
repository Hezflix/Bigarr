using System.Net;
using System.Text;
using System.Text.Json;
using Bigarr.Domain.MediaServers;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>
/// Jellyfin and Emby share the same API for what Bigarr needs: <c>/System/Info</c> to test, <c>/Library/Media/Updated</c>
/// to point at one changed folder, <c>/Library/Refresh</c> for a full scan. The API key travels as <c>X-Emby-Token</c>.
/// </summary>
public sealed class EmbyClient(IHttpClientFactory httpClientFactory) : IMediaServerClient
{
    public bool Handles(MediaServerKind kind) => kind is MediaServerKind.Jellyfin or MediaServerKind.Emby;

    public async Task<MediaServerInfo> TestAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var info = await GetJsonAsync(server, "/System/Info", ct);
        var name = info.RootElement.TryGetProperty("ServerName", out var n) ? n.GetString() : null;
        var version = info.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;

        var libraries = 0;
        using var folders = await GetJsonAsync(server, "/Library/VirtualFolders", ct);
        if (folders.RootElement.ValueKind == JsonValueKind.Array)
        {
            libraries = folders.RootElement.EnumerateArray().Count(f => f.TryGetProperty("CollectionType", out var c) && string.Equals(c.GetString(), "movies", StringComparison.OrdinalIgnoreCase));
        }

        return new MediaServerInfo(name, version, libraries);
    }

    public async Task NotifyAsync(MediaServer server, string path, MediaChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(path);

        // PascalCase, as the Jellyfin and Emby docs spell it; both bind case-insensitively anyway.
        var body = JsonSerializer.Serialize(new { Updates = new[] { new { Path = path, UpdateType = change.ToString() } } });
        using var response = await SendAsync(server, HttpMethod.Post, "/Library/Media/Updated", body, ct);
    }

    public async Task RefreshAllAsync(MediaServer server, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        using var response = await SendAsync(server, HttpMethod.Post, "/Library/Refresh", null, ct);
    }

    private async Task<JsonDocument> GetJsonAsync(MediaServer server, string path, CancellationToken ct)
    {
        using var response = await SendAsync(server, HttpMethod.Get, path, null, ct);
        try
        {
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new MediaServerException($"{server.Kind} answered something that is not JSON on {path}: is the URL a {server.Kind} server?", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(MediaServer server, HttpMethod method, string path, string? jsonBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, server.BaseUrl + path);
        var token = server.Token.Trim();
        request.Headers.TryAddWithoutValidation("X-Emby-Token", token);
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Client=\"Bigarr\", Device=\"Bigarr\", DeviceId=\"bigarr\", Version=\"{Features.Server.SystemEndpoints.Version}\", Token=\"{token}\"");
        request.Headers.Accept.ParseAdd("application/json");
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

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
            throw new MediaServerException(ex is TaskCanceledException ? $"{server.Kind} did not answer before the timeout." : $"{server.Kind} could not be reached: {ex.Message}", ex);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new MediaServerException($"{server.Kind} refused the API key (HTTP 401).");
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new MediaServerException($"{server.Kind} answered HTTP {status} on {path}.");
        }

        return response;
    }
}
