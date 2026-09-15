using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Infrastructure.Downloads.QBittorrent;

/// <summary>
/// qBittorrent WebUI API v2 (4.1+). One session cookie per configured client, renewed on 403. Torrent files are
/// downloaded here first so the info hash is known before qBittorrent lists the torrent: that hash is the download id.
/// </summary>
public sealed class QBittorrentClient(IHttpClientFactory httpClientFactory, TorrentFileFetcher torrents, TimeProvider time, ILogger<QBittorrentClient> logger) : IDownloadClient
{
    public const string HttpClientName = "qbittorrent";

    /// <summary>qBittorrent's "no ETA" marker.</summary>
    public const long UnknownEta = 8_640_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public DownloadClientImplementation Implementation => DownloadClientImplementation.QBittorrent;

    public async Task<DownloadClientInfo> TestAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var version = (await GetStringAsync(client, "/api/v2/app/version", cancellationToken)).Trim();
        var apiVersion = (await GetStringAsync(client, "/api/v2/app/webapiVersion", cancellationToken)).Trim();
        if (version.Length == 0 || version.Length > 32 || version.Contains('<', StringComparison.Ordinal))
        {
            throw new DownloadClientException(DownloadClientFailureKind.Request, $"{client.BaseUrl} answered but not like qBittorrent's API. Check the URL base.");
        }

        var created = await EnsureCategoryAsync(client, cancellationToken);
        return new DownloadClientInfo(version, apiVersion, client.Category, created);
    }

    public async Task<string> AddAsync(DownloadClient client, DownloadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        var hash = Normalize(request.InfoHash);
        byte[]? file = null;
        string? magnet = null;

        if (MagnetLink.IsMagnet(request.MagnetUrl))
        {
            magnet = request.MagnetUrl;
            hash ??= MagnetLink.TryGetInfoHash(magnet);
        }
        else if (MagnetLink.IsMagnet(request.DownloadUrl))
        {
            magnet = request.DownloadUrl;
            hash ??= MagnetLink.TryGetInfoHash(magnet);
        }
        else if (!string.IsNullOrWhiteSpace(request.DownloadUrl))
        {
            var fetched = await torrents.FetchAsync(request.DownloadUrl, cancellationToken);
            file = fetched.File;
            magnet = fetched.Magnet;
            hash = fetched.InfoHash; // the file knows better than the indexer
        }
        else if (hash is not null)
        {
            magnet = MagnetLink.Build(hash, request.Title);
        }
        else
        {
            throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "The release has no download link, no magnet and no info hash.");
        }

        if (hash is null)
        {
            throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "The magnet link carries no info hash; qBittorrent could not be asked about it afterwards.");
        }

        await EnsureCategoryAsync(client, cancellationToken);

        using var form = new MultipartFormDataContent();
        if (file is not null)
        {
            var torrent = new ByteArrayContent(file);
            torrent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(torrent, "torrents", $"{hash}.torrent");
        }
        else
        {
            form.Add(new StringContent(magnet!), "urls");
        }

        form.Add(new StringContent(client.Category), "category");
        // 4.x reads "paused", 5.x reads "stopped"; the other one is ignored.
        form.Add(new StringContent(client.AddPaused ? "true" : "false"), "paused");
        form.Add(new StringContent(client.AddPaused ? "true" : "false"), "stopped");
        if (request.SeedRatio is { } ratio)
        {
            form.Add(new StringContent(ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)), "ratioLimit");
        }

        if (request.SeedTime is { } seedTime)
        {
            form.Add(new StringContent(((long)seedTime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture)), "seedingTimeLimit");
        }

        var answer = await PostAsync(client, "/api/v2/torrents/add", form, cancellationToken);
        if (answer.Contains("Fails", StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadClientException(DownloadClientFailureKind.Request, "qBittorrent refused the torrent: already present or invalid.");
        }

        // Adding is asynchronous on qBittorrent's side; a magnet needs its metadata before it shows up in some versions.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var listed = await GetTorrentsAsync(client, hash, cancellationToken);
            if (listed.Any(t => string.Equals(t.Hash, hash, StringComparison.OrdinalIgnoreCase)))
            {
                return hash;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), time, cancellationToken);
        }

        logger.LogInformation("qBittorrent accepted {Hash} but does not list it yet; the queue tracker will pick it up", hash);
        return hash;
    }

    public async Task<IReadOnlyList<DownloadItem>> GetItemsAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        var listed = await GetJsonAsync<List<QBittorrentTorrent>>(client, $"/api/v2/torrents/info?category={Uri.EscapeDataString(client.Category)}", cancellationToken);
        return listed.Select(Map).ToList();
    }

    public async Task RemoveAsync(DownloadClient client, string downloadId, bool deleteFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["hashes"] = downloadId.ToLowerInvariant(),
            ["deleteFiles"] = deleteFiles ? "true" : "false",
        });
        await PostAsync(client, "/api/v2/torrents/delete", form, cancellationToken);
    }

    /// <summary>qBittorrent's torrent state, reduced to what the queue distinguishes.</summary>
    public static DownloadItemStatus StatusOf(string state) => state switch
    {
        "error" or "missingFiles" => DownloadItemStatus.Failed,
        "pausedDL" or "stoppedDL" => DownloadItemStatus.Paused,
        "queuedDL" or "checkingDL" or "checkingResumeData" or "allocating" or "metaDL" or "forcedMetaDL" => DownloadItemStatus.Queued,
        "downloading" or "forcedDL" => DownloadItemStatus.Downloading,
        "stalledDL" => DownloadItemStatus.Stalled,
        "uploading" or "stalledUP" or "queuedUP" or "pausedUP" or "stoppedUP" or "forcedUP" or "checkingUP" or "moving" => DownloadItemStatus.Completed,
        _ => DownloadItemStatus.Unknown,
    };

    public static DownloadItem Map(QBittorrentTorrent torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        var status = StatusOf(torrent.State);
        var message = torrent.State switch
        {
            "error" => "qBittorrent reports an error on this torrent.",
            "missingFiles" => "qBittorrent cannot find the files of this torrent.",
            "moving" => "qBittorrent is moving the files.",
            "stalledDL" => "No connection to any peer.",
            "metaDL" or "forcedMetaDL" => "Fetching metadata.",
            "checkingDL" or "checkingUP" or "checkingResumeData" => "Checking files.",
            _ => null,
        };

        var total = torrent.TotalSize > 0 ? torrent.TotalSize : torrent.Size;
        return new DownloadItem(
            torrent.Hash.ToLowerInvariant(),
            torrent.Name,
            total,
            Math.Max(0, torrent.AmountLeft),
            Math.Clamp(torrent.Progress, 0, 1),
            status,
            torrent.Eta is > 0 and < UnknownEta ? TimeSpan.FromSeconds(torrent.Eta) : null,
            Math.Max(0, torrent.DownloadSpeed),
            string.IsNullOrWhiteSpace(torrent.ContentPath) ? torrent.SavePath : torrent.ContentPath,
            message,
            torrent.Ratio >= 0 ? torrent.Ratio : null,
            torrent.SeedingTime > 0 ? TimeSpan.FromSeconds(torrent.SeedingTime) : null,
            torrent.NumSeeds,
            torrent.AddedOn > 0 ? DateTimeOffset.FromUnixTimeSeconds(torrent.AddedOn) : null,
            torrent.CompletionOn > 0 ? DateTimeOffset.FromUnixTimeSeconds(torrent.CompletionOn) : null);
    }

    private async Task<List<QBittorrentTorrent>> GetTorrentsAsync(DownloadClient client, string hash, CancellationToken cancellationToken) =>
        await GetJsonAsync<List<QBittorrentTorrent>>(client, $"/api/v2/torrents/info?hashes={Uri.EscapeDataString(hash)}", cancellationToken);

    /// <summary>True when the category had to be created.</summary>
    private async Task<bool> EnsureCategoryAsync(DownloadClient client, CancellationToken cancellationToken)
    {
        var categories = await GetJsonAsync<Dictionary<string, QBittorrentCategory>>(client, "/api/v2/torrents/categories", cancellationToken);
        if (categories.ContainsKey(client.Category))
        {
            return false;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["category"] = client.Category });
        using var response = await SendAsync(client, () => new HttpRequestMessage(HttpMethod.Post, client.BaseUrl + "/api/v2/torrents/createCategory") { Content = form }, cancellationToken, allowConflict: true);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return false; // created meanwhile
        }

        logger.LogInformation("Created category {Category} in qBittorrent {Client}", client.Category, client.Name);
        return true;
    }

    private async Task<string> GetStringAsync(DownloadClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, () => new HttpRequestMessage(HttpMethod.Get, client.BaseUrl + path), cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<T> GetJsonAsync<T>(DownloadClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, () => new HttpRequestMessage(HttpMethod.Get, client.BaseUrl + path), cancellationToken);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
                   ?? throw new DownloadClientException(DownloadClientFailureKind.Request, "qBittorrent returned an empty document.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new DownloadClientException(DownloadClientFailureKind.Request, $"qBittorrent answered with something that is not its API: {ex.Message}", ex);
        }
    }

    private async Task<string> PostAsync(DownloadClient client, string path, HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, () => new HttpRequestMessage(HttpMethod.Post, client.BaseUrl + path) { Content = content }, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Sends with the session cookie, logging in first when needed and once more after a 403.</summary>
    private async Task<HttpResponseMessage> SendAsync(DownloadClient client, Func<HttpRequestMessage> request, CancellationToken cancellationToken, bool allowConflict = false)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var key = SessionKey(client);
        var hasCredentials = !string.IsNullOrEmpty(client.Username);

        for (var attempt = 0; ; attempt++)
        {
            if (!_sessions.TryGetValue(key, out var sid) && hasCredentials)
            {
                sid = await LoginAsync(http, client, cancellationToken);
                _sessions[key] = sid;
            }

            var message = request();
            message.Headers.Referrer = new Uri(client.BaseUrl + "/");
            if (sid is not null)
            {
                message.Headers.TryAddWithoutValidation("Cookie", $"SID={sid}");
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadClientException(DownloadClientFailureKind.Unreachable, $"Could not reach qBittorrent at {client.BaseUrl}: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DownloadClientException(DownloadClientFailureKind.Unreachable, $"qBittorrent at {client.BaseUrl} did not answer within {http.Timeout.TotalSeconds:0} seconds.", ex);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 0 && hasCredentials)
            {
                // Expired session: log in again, once.
                response.Dispose();
                _sessions.TryRemove(key, out _);
                continue;
            }

            if (allowConflict && response.StatusCode == HttpStatusCode.Conflict)
            {
                return response;
            }

            try
            {
                ThrowIfFailed(response, client, hasCredentials);
            }
            catch
            {
                response.Dispose();
                throw;
            }

            return response;
        }
    }

    private static async Task<string> LoginAsync(HttpClient http, DownloadClient client, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = client.Username ?? string.Empty,
            ["password"] = client.Password ?? string.Empty,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, client.BaseUrl + "/api/v2/auth/login") { Content = form };
        request.Headers.Referrer = new Uri(client.BaseUrl + "/");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DownloadClientException(DownloadClientFailureKind.Unreachable, $"Could not reach qBittorrent at {client.BaseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadClientException(DownloadClientFailureKind.Unreachable, $"qBittorrent at {client.BaseUrl} did not answer within {http.Timeout.TotalSeconds:0} seconds.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new DownloadClientException(DownloadClientFailureKind.Authentication, "qBittorrent banned this address after too many failed logins. Wait or restart qBittorrent.");
            }

            ThrowIfFailed(response, client, hasCredentials: true);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (!body.Equals("Ok.", StringComparison.OrdinalIgnoreCase))
            {
                throw new DownloadClientException(DownloadClientFailureKind.Authentication, "qBittorrent refused the username or the password.");
            }

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var part = cookie.Split(';', 2)[0].Trim();
                    if (part.StartsWith("SID=", StringComparison.Ordinal))
                    {
                        return part["SID=".Length..];
                    }
                }
            }

            throw new DownloadClientException(DownloadClientFailureKind.Authentication, "qBittorrent accepted the login but sent no session cookie.");
        }
    }

    private static void ThrowIfFailed(HttpResponseMessage response, DownloadClient client, bool hasCredentials)
    {
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            var target = response.Headers.Location?.Host;
            throw new DownloadClientException(
                DownloadClientFailureKind.Authentication,
                target is null
                    ? $"qBittorrent redirected the request (HTTP {status}): a login page or a proxy is in front of it."
                    : $"qBittorrent redirected to {target} (HTTP {status}): a login page is in front of it. Use an internal URL or exempt /api/v2.");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                throw new DownloadClientException(
                    DownloadClientFailureKind.Authentication,
                    hasCredentials
                        ? $"HTTP {status}: qBittorrent refused the session. Check the username and the password."
                        : $"HTTP {status}: qBittorrent requires a login and no username is configured.");
            case HttpStatusCode.NotFound:
                throw new DownloadClientException(DownloadClientFailureKind.Request, $"HTTP 404 from {client.BaseUrl}: not qBittorrent's API. Check the port and the URL base.");
            case HttpStatusCode.UnsupportedMediaType:
                throw new DownloadClientException(DownloadClientFailureKind.InvalidRelease, "qBittorrent says the torrent file is not valid.");
            case >= HttpStatusCode.InternalServerError:
                throw new DownloadClientException(DownloadClientFailureKind.Request, $"HTTP {status}: qBittorrent failed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadClientException(DownloadClientFailureKind.Request, $"HTTP {status} from qBittorrent.");
        }
    }

    private static string SessionKey(DownloadClient client) => $"{client.BaseUrl}|{client.Username}";

    private static string? Normalize(string? infoHash) => InfoHashes.Normalize(infoHash);
}
