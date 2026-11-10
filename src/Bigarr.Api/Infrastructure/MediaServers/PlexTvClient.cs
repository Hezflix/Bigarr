using System.Net;
using System.Text.Json;

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
/// plex.tv's own API with a server owner's token. Only the geolocation of an address for now
/// (<c>/api/v2/geoip?ip_address=</c>), the lookup Plex Web and Tautulli use.
/// </summary>
public sealed class PlexTvClient(HttpClient http)
{
    public const string HttpClientName = "bigarr-plextv";

    /// <summary>Where plex.tv places <paramref name="ipAddress"/>; null when it has no idea.</summary>
    public async Task<GeoLocation?> GeoIpAsync(string token, string ipAddress, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipAddress);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v2/geoip?ip_address=" + Uri.EscapeDataString(ipAddress));
        request.Headers.TryAddWithoutValidation("X-Plex-Token", token.Trim());
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", "bigarr");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Bigarr");
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MediaServerException(ex is TaskCanceledException ? "plex.tv did not answer before the timeout." : $"plex.tv could not be reached: {ex.Message}", ex);
        }

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
