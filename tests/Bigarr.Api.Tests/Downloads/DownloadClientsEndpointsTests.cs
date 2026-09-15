using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Downloads;

namespace Bigarr.Api.Tests.Downloads;

public sealed class DownloadClientsEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Download_clients_are_created_listed_updated_tested_and_deleted()
    {
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync("/api/v1/downloadclients",
            new DownloadClientRequest("Home qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Port: 8080, UrlBase: "qbittorrent/", Username: "admin", Password: "secret", Category: "bigarr-movies", Priority: 2), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var qbt = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        Assert.Equal("Home qBittorrent", qbt.Name);
        Assert.Equal("/qbittorrent", qbt.UrlBase);
        Assert.Equal("admin", qbt.Username);
        Assert.True(qbt.Enabled);
        Assert.Equal(2, qbt.Priority);

        var raw = await client.GetStringAsync($"/api/v1/downloadclients/{qbt.Id}");
        Assert.Contains("\"implementation\":\"qBittorrent\"", raw);
        Assert.Contains("\"protocol\":\"torrent\"", raw);

        var duplicate = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Home qBittorrent", DownloadClientImplementation.Mock), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var mockCreated = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Mock", DownloadClientImplementation.Mock, Host: "ignored", Username: "ignored", Priority: 1), Json);
        var mock = (await mockCreated.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        Assert.Null(mock.Host); // qBittorrent-only fields are dropped
        Assert.Null(mock.Username);
        Assert.Equal(DownloadClient.DefaultCategory, mock.Category);

        var list = (await client.GetFromJsonAsync<List<DownloadClientResponse>>("/api/v1/downloadclients", Json))!;
        Assert.Equal([mock.Id, qbt.Id], list.Select(c => c.Id)); // by priority

        var updated = await client.PutAsJsonAsync($"/api/v1/downloadclients/{qbt.Id}",
            new DownloadClientRequest("Renamed", DownloadClientImplementation.QBittorrent, Enabled: false, Host: "10.0.0.5", Port: 9090, UseSsl: true, Category: "films", AddPaused: true, Priority: 5), Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var renamed = (await updated.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        Assert.Equal("Renamed", renamed.Name);
        Assert.False(renamed.Enabled);
        Assert.True(renamed.UseSsl);
        Assert.Null(renamed.UrlBase);
        Assert.Equal("films", renamed.Category);
        Assert.True(renamed.AddPaused);

        // The mock answers the test; a qBittorrent that is not there does not.
        var tested = await client.PostAsync($"/api/v1/downloadclients/{mock.Id}/test", null);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var info = (await tested.Content.ReadFromJsonAsync<DownloadClientTestResponse>(Json))!;
        Assert.Equal("mock", info.Version);
        Assert.Equal(DownloadClient.DefaultCategory, info.Category);

        var dead = await client.PostAsJsonAsync("/api/v1/downloadclients/test",
            new DownloadClientRequest("Dead", DownloadClientImplementation.QBittorrent, Host: "127.0.0.1", Port: 1), Json);
        Assert.Equal(HttpStatusCode.BadRequest, dead.StatusCode);
        Assert.Contains("Could not reach qBittorrent", await dead.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/downloadclients/{qbt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/downloadclients/{mock.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/downloadclients/{qbt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/downloadclients/{qbt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/downloadclients/999999/test", null)).StatusCode);
    }

    [Fact]
    public async Task Configurations_are_validated()
    {
        var client = await ClientAsync();

        async Task<string> Refused(DownloadClientRequest request)
        {
            var response = await client.PostAsJsonAsync("/api/v1/downloadclients", request, Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("Name is required", await Refused(new DownloadClientRequest(" ", DownloadClientImplementation.Mock)));
        Assert.Contains("Invalid host", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.QBittorrent)));
        Assert.Contains("Invalid host", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.QBittorrent, Host: "http://qbt:8080")));
        Assert.Contains("Invalid port", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.QBittorrent, Host: "qbt", Port: 0)));
        Assert.Contains("Invalid port", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.QBittorrent, Host: "qbt", Port: 70000)));
        Assert.Contains("Invalid category", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.Mock, Category: "")));
        Assert.Contains("Invalid category", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.Mock, Category: "a/b")));
        Assert.Contains("Invalid priority", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.Mock, Priority: 0)));
        Assert.Contains("Invalid priority", await Refused(new DownloadClientRequest("q", DownloadClientImplementation.Mock, Priority: 51)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync("/api/v1/downloadclients")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync("/api/v1/queue")).StatusCode);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
