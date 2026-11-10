using System.Text.Json;
using Bigarr.Api.Tests.Hosting;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// plex.tv's geolocation with invented places for documentation addresses: <c>/api/v2/geoip?ip_address=</c> answers
/// like plex.tv does, <c>Unknown</c> everywhere for an address it has no place for.
/// </summary>
public sealed class FakePlexTv : IAsyncDisposable
{
    private readonly FakeHttpServer _http;

    private FakePlexTv(FakeHttpServer http)
    {
        _http = http;
        http.Responder = Respond;
    }

    public sealed record Place(string City, string Region, string Country, string Code);

    public string Url => _http.Url;

    public Dictionary<string, Place> Places { get; } = new()
    {
        ["198.51.100.7"] = new Place("Lyon", "Auvergne-Rhône-Alpes", "France", "FR"),
        ["203.0.113.20"] = new Place("Porto", "Porto", "Portugal", "PT"),
    };

    /// <summary>Answer HTTP 503: Bigarr must carry on without locations.</summary>
    public bool Down { get; set; }

    public IEnumerable<FakeHttpServer.Received> Requests => _http.Requests;

    public int Lookups(string ip) => _http.Requests.Count(r => r.Path == "/api/v2/geoip" && r.Query == "?ip_address=" + ip);

    public static async Task<FakePlexTv> StartAsync() => new(await FakeHttpServer.StartAsync());

    private FakeHttpServer.Answer? Respond(FakeHttpServer.Received request)
    {
        if (!request.Headers.TryGetValue("X-Plex-Token", out var token) || token != FakePlex.Token)
        {
            return new FakeHttpServer.Answer(401);
        }

        if (request.Path != "/api/v2/geoip")
        {
            return null;
        }

        if (Down)
        {
            return new FakeHttpServer.Answer(503);
        }

        var ip = Uri.UnescapeDataString(request.Query.TrimStart('?').Replace("ip_address=", string.Empty, StringComparison.Ordinal));
        object body = Places.TryGetValue(ip, out var place)
            ? new { code = place.Code, country = place.Country, city = place.City, subdivisions = place.Region, time_zone = "Etc/UTC", coordinates = "0.0, 0.0" }
            : new { code = "ZZ", country = "Unknown", city = "Unknown", subdivisions = "Unknown", time_zone = "Etc/UTC", coordinates = "0.0, 0.0" };
        return new FakeHttpServer.Answer(200, JsonSerializer.Serialize(body));
    }

    public async ValueTask DisposeAsync() => await _http.DisposeAsync();
}
