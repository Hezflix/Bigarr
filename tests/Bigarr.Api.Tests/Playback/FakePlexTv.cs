using System.Text.Json;
using Bigarr.Api.Tests.Hosting;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// plex.tv's geolocation with invented places for documentation addresses: <c>/api/v2/geoip?ip_address=</c> answers
/// like plex.tv does, <c>Unknown</c> everywhere for an address it has no place for. <c>/api/users</c> lists invented
/// friends in plex.tv's XML.
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

    /// <summary>An account the owner shares with; <c>Servers</c> are machine identifiers, <c>Pending</c> an invitation.</summary>
    public sealed record Friend(string Id, string Username, string[] Servers, bool Home = false, bool Pending = false);

    public List<Friend> Friends { get; } = [];

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

        if (Down && request.Path is "/api/v2/geoip" or "/api/users")
        {
            return new FakeHttpServer.Answer(503);
        }

        if (request.Path == "/api/users")
        {
            var users = new System.Xml.Linq.XElement("MediaContainer", new System.Xml.Linq.XAttribute("friendlyName", "myPlex"), new System.Xml.Linq.XAttribute("size", Friends.Count));
            foreach (var friend in Friends)
            {
                users.Add(new System.Xml.Linq.XElement(
                    "User",
                    new System.Xml.Linq.XAttribute("id", friend.Id),
                    new System.Xml.Linq.XAttribute("title", friend.Username),
                    new System.Xml.Linq.XAttribute("username", friend.Username),
                    new System.Xml.Linq.XAttribute("home", friend.Home ? "1" : "0"),
                    friend.Servers.Select((m, i) => new System.Xml.Linq.XElement(
                        "Server",
                        new System.Xml.Linq.XAttribute("id", i + 1),
                        new System.Xml.Linq.XAttribute("machineIdentifier", m),
                        new System.Xml.Linq.XAttribute("name", "Server " + m),
                        new System.Xml.Linq.XAttribute("owned", "1"),
                        new System.Xml.Linq.XAttribute("pending", friend.Pending ? "1" : "0")))));
            }

            return new FakeHttpServer.Answer(200, users.ToString());
        }

        if (request.Path != "/api/v2/geoip")
        {
            return null;
        }

        var ip = Uri.UnescapeDataString(request.Query.TrimStart('?').Replace("ip_address=", string.Empty, StringComparison.Ordinal));
        object body = Places.TryGetValue(ip, out var place)
            ? new { code = place.Code, country = place.Country, city = place.City, subdivisions = place.Region, time_zone = "Etc/UTC", coordinates = "0.0, 0.0" }
            : new { code = "ZZ", country = "Unknown", city = "Unknown", subdivisions = "Unknown", time_zone = "Etc/UTC", coordinates = "0.0, 0.0" };
        return new FakeHttpServer.Answer(200, JsonSerializer.Serialize(body));
    }

    public async ValueTask DisposeAsync() => await _http.DisposeAsync();
}
