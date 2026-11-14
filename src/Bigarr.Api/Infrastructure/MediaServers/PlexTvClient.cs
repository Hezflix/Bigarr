using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>Where plex.tv places an address. Every field is optional.</summary>
public sealed record GeoLocation(string? City, string? Region, string? Country, string? CountryCode)
{
    public bool IsEmpty => City is null && Region is null && Country is null && CountryCode is null;
}

/// <summary>Bound from <c>Bigarr:PlexTv</c>: where plex.tv is, so the tests can stand in for it.</summary>
public sealed class PlexTvOptions
{
    public const string Section = "Bigarr:PlexTv";

    public string BaseUrl { get; set; } = "https://plex.tv";
}

/// <summary>
/// A plex.tv account the owner shares with (<c>/api/users</c>): <c>Servers</c> are the machine identifiers of the
/// owner's servers it was given, an invitation not yet accepted left out; <c>Home</c> is a Plex Home member.
/// </summary>
public sealed record PlexTvFriend(string Id, string? Username, bool Home, IReadOnlyList<string> Servers);

/// <summary>
/// A sign-in PIN (<c>/api/v2/pins</c>): the person approves <c>Code</c> on plex.tv's auth page, after which reading
/// the PIN back gives <c>AuthToken</c>. Null until then.
/// </summary>
public sealed record PlexTvPin(long Id, string Code, DateTimeOffset? ExpiresAt, string? AuthToken);

/// <summary>The plex.tv account behind a token (<c>/api/v2/user</c>): <c>Id</c> is the numeric account id.</summary>
public sealed record PlexTvAccount(string Id, string Username, string? Email, string? Thumb);

/// <summary>
/// plex.tv's own API with a server owner's token: the geolocation of an address (<c>/api/v2/geoip?ip_address=</c>,
/// the lookup Plex Web and Tautulli use) and the accounts the owner shares with (<c>/api/users</c>, Tautulli's list).
/// Without a token: the sign-in PINs (<c>/api/v2/pins</c>) and, with the token one gives, the account behind it.
/// </summary>
public sealed class PlexTvClient(HttpClient http)
{
    public const string HttpClientName = "bigarr-plextv";

    /// <summary>How Bigarr introduces itself to plex.tv; a PIN is read back with the identifier that created it.</summary>
    public const string ClientIdentifier = "bigarr";

    public const string Product = "Bigarr";

    /// <summary>Where plex.tv places <paramref name="ipAddress"/>; null when it has no idea.</summary>
    public async Task<GeoLocation?> GeoIpAsync(string token, string ipAddress, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipAddress);

        using var request = Request("api/v2/geoip?ip_address=" + Uri.EscapeDataString(ipAddress), token, "application/json");
        var response = await SendAsync(request, "the geolocation", ct);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MediaServerException($"plex.tv answered HTTP {(int)response.StatusCode} to the geolocation.") { StatusCode = response.StatusCode };
            }

            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return ParseGeoIp(doc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new MediaServerException("plex.tv answered something that is not JSON to the geolocation.", ex);
            }
        }
    }

    /// <summary>The owner's friends and Plex Home members, with the servers shared with each.</summary>
    public async Task<List<PlexTvFriend>> FriendsAsync(string token, CancellationToken ct)
    {
        using var request = Request("api/users", token, "application/xml");
        using var response = await SendAsync(request, "the list of users", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MediaServerException($"plex.tv answered HTTP {(int)response.StatusCode} to the list of users.") { StatusCode = response.StatusCode };
        }

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return ParseFriends(await XDocument.LoadAsync(body, LoadOptions.None, ct));
        }
        catch (XmlException ex)
        {
            throw new MediaServerException("plex.tv answered something that is not XML to the list of users.", ex);
        }
    }

    /// <summary>A new sign-in PIN. <c>strong</c> PINs are the ones plex.tv's auth page approves.</summary>
    public async Task<PlexTvPin> CreatePinAsync(CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "api/v2/pins?strong=true", token: null, "application/json");
        using var response = await SendAsync(request, "the sign-in PIN", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MediaServerException($"plex.tv answered HTTP {(int)response.StatusCode} to the sign-in PIN.") { StatusCode = response.StatusCode };
        }

        return ParsePin(await ReadJsonAsync(response, "the sign-in PIN", ct))
            ?? throw new MediaServerException("plex.tv answered a sign-in PIN without an id or a code.");
    }

    /// <summary>The PIN as plex.tv has it now; null once it is gone (expired, or never was).</summary>
    public async Task<PlexTvPin?> PinAsync(long id, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "api/v2/pins/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), token: null, "application/json");
        using var response = await SendAsync(request, "the sign-in PIN", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MediaServerException($"plex.tv answered HTTP {(int)response.StatusCode} to the sign-in PIN.") { StatusCode = response.StatusCode };
        }

        return ParsePin(await ReadJsonAsync(response, "the sign-in PIN", ct));
    }

    /// <summary>The account <paramref name="token"/> belongs to; null when plex.tv refuses the token.</summary>
    public async Task<PlexTvAccount?> AccountAsync(string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        using var request = Request(HttpMethod.Get, "api/v2/user", token, "application/json");
        using var response = await SendAsync(request, "the account", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MediaServerException($"plex.tv answered HTTP {(int)response.StatusCode} to the account.") { StatusCode = response.StatusCode };
        }

        return ParseAccount(await ReadJsonAsync(response, "the account", ct))
            ?? throw new MediaServerException("plex.tv answered an account without an id or a username.");
    }

    /// <summary><c>{ "id": 123, "code": "abcd", "expiresAt": "…", "authToken": null }</c></summary>
    public static PlexTvPin? ParsePin(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var pinId)
            || Text(root, "code") is not { } code)
        {
            return null;
        }

        DateTimeOffset? expiresAt = root.TryGetProperty("expiresAt", out var expires) && expires.ValueKind == JsonValueKind.String && expires.TryGetDateTimeOffset(out var at) ? at : null;
        return new PlexTvPin(pinId, code, expiresAt, Text(root, "authToken"));
    }

    /// <summary><c>{ "id": 123, "uuid": "…", "username": "…", "title": "…", "email": "…", "thumb": "…" }</c></summary>
    public static PlexTvAccount? ParseAccount(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id))
        {
            return null;
        }

        var accountId = id.ValueKind switch
        {
            JsonValueKind.Number => id.GetRawText(),
            JsonValueKind.String => id.GetString()?.Trim(),
            _ => null,
        };
        var username = Text(root, "username") ?? Text(root, "title");
        return string.IsNullOrEmpty(accountId) || username is null
            ? null
            : new PlexTvAccount(accountId, username, Text(root, "email"), Text(root, "thumb"));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new MediaServerException($"plex.tv answered something that is not JSON to {what}.", ex);
        }
    }

    /// <summary><c>&lt;MediaContainer&gt;&lt;User id username home&gt;&lt;Server machineIdentifier pending/&gt;…</c></summary>
    public static List<PlexTvFriend> ParseFriends(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Root?.Elements("User")
            .Select(u => (Id: (string?)u.Attribute("id"), User: u))
            .Where(u => !string.IsNullOrWhiteSpace(u.Id))
            .Select(u => new PlexTvFriend(
                u.Id!.Trim(),
                (string?)u.User.Attribute("username") ?? (string?)u.User.Attribute("title"),
                (string?)u.User.Attribute("home") == "1",
                u.User.Elements("Server")
                    .Where(s => (string?)s.Attribute("pending") != "1")
                    .Select(s => (string?)s.Attribute("machineIdentifier"))
                    .OfType<string>()
                    .ToList()))
            .ToList() ?? [];
    }

    private static HttpRequestMessage Request(string path, string token, string accept) => Request(HttpMethod.Get, path, token, accept);

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token, string accept)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token.Trim());
        }

        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", ClientIdentifier);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", Product);
        request.Headers.Accept.ParseAdd(accept);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MediaServerException(ex is TaskCanceledException ? $"plex.tv did not answer {what} before the timeout." : $"plex.tv could not be reached: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The flat object plex.tv answers (<c>city</c>, <c>subdivisions</c>, <c>country</c>, <c>code</c>), or the same under
    /// <c>location</c>. An address it cannot place comes back as <c>Unknown</c> everywhere and code <c>ZZ</c>.
    /// </summary>
    public static GeoLocation? ParseGeoIp(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("location", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            root = nested;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var location = new GeoLocation(
            Text(root, "city"),
            Text(root, "subdivisions"),
            Text(root, "country"),
            Text(root, "code")?.ToUpperInvariant() is { } code and not "ZZ" ? code : null);
        return location.IsEmpty ? null : location;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text && !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? text
            : null;
}
