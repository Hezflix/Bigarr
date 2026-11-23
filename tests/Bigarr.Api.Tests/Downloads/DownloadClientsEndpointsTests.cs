using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.Downloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task The_password_is_never_sent_back_and_the_mask_keeps_it()
    {
        var client = await ClientAsync();
        const string mask = "********";

        var created = await client.PostAsJsonAsync("/api/v1/downloadclients",
            new DownloadClientRequest("Masked qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: "placeholder-secret", Category: "masked-movies", Priority: 7), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var added = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        Assert.Equal(mask, added.Password);

        Assert.Equal(mask, (await client.GetFromJsonAsync<DownloadClientResponse>($"/api/v1/downloadclients/{added.Id}", Json))!.Password);
        var list = (await client.GetFromJsonAsync<List<DownloadClientResponse>>("/api/v1/downloadclients", Json))!;
        Assert.Equal(mask, list.Single(c => c.Id == added.Id).Password);
        Assert.DoesNotContain("placeholder-secret", await client.GetStringAsync("/api/v1/downloadclients"));

        // The mask sent back means "unchanged".
        var kept = await client.PutAsJsonAsync($"/api/v1/downloadclients/{added.Id}",
            new DownloadClientRequest("Masked qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: mask, Category: "masked-films", Priority: 7), Json);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        var stored = await StoredAsync(added.Id);
        Assert.Equal("placeholder-secret", stored.Password);
        Assert.Equal("masked-films", stored.Category);

        // A new value replaces it, an empty one clears it.
        var replaced = await client.PutAsJsonAsync($"/api/v1/downloadclients/{added.Id}",
            new DownloadClientRequest("Masked qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: "other-placeholder", Category: "masked-films", Priority: 7), Json);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal("other-placeholder", (await StoredAsync(added.Id)).Password);

        var cleared = await client.PutAsJsonAsync($"/api/v1/downloadclients/{added.Id}",
            new DownloadClientRequest("Masked qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: "", Category: "masked-films", Priority: 7), Json);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!.Password);
        Assert.Null((await StoredAsync(added.Id)).Password);

        // The mask cannot be the secret of a new client, nor of a test that has no saved client to take it from.
        var refused = await client.PostAsJsonAsync("/api/v1/downloadclients",
            new DownloadClientRequest("Masked again", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: mask, Category: "masked-movies", Priority: 8), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var refusedTest = await client.PostAsJsonAsync("/api/v1/downloadclients/test",
            new DownloadClientRequest("Masked again", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: mask), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refusedTest.StatusCode);
        Assert.Contains("Type the password", await refusedTest.Content.ReadAsStringAsync());
        var unknown = await client.PostAsJsonAsync("/api/v1/downloadclients/test?id=999999",
            new DownloadClientRequest("Masked again", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Password: mask), Json);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        await client.DeleteAsync($"/api/v1/downloadclients/{added.Id}");
    }

    [Fact]
    public async Task Changing_the_address_with_the_mask_is_refused_and_the_stored_password_stays()
    {
        var client = await ClientAsync();
        const string mask = "********";

        var created = await client.PostAsJsonAsync("/api/v1/downloadclients",
            new DownloadClientRequest("Moving qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Port: 8080, Username: "admin", Password: "placeholder-secret", Category: "moving-movies", Priority: 21), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!.Id;

        DownloadClientRequest Form(string name = "Moving qBittorrent", string host = "qbt.local", int port = 8080, bool useSsl = false, string username = "admin", string? password = mask) =>
            new(name, DownloadClientImplementation.QBittorrent, Host: host, Port: port, UseSsl: useSsl, Username: username, Password: password, Category: "moving-movies", Priority: 21);

        async Task Refused(DownloadClientRequest request)
        {
            var response = await client.PutAsJsonAsync($"/api/v1/downloadclients/{id}", request, Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Type the password again", await response.Content.ReadAsStringAsync());
        }

        // Another host, another port, or switching SSL on or off: the mask no longer stands for the stored password.
        await Refused(Form(host: "elsewhere.local"));
        await Refused(Form(port: 9090));
        await Refused(Form(useSsl: true));
        var stored = await StoredAsync(id);
        Assert.Equal("qbt.local", stored.Host);
        Assert.Equal(8080, stored.Port);
        Assert.False(stored.UseSsl);
        Assert.Equal("placeholder-secret", stored.Password);
        Assert.Equal("qbt.local", (await client.GetFromJsonAsync<DownloadClientResponse>($"/api/v1/downloadclients/{id}", Json))!.Host);

        // The same address (host case and spaces do not count) and another name or username keep the password.
        var same = await client.PutAsJsonAsync($"/api/v1/downloadclients/{id}", Form(name: "Moving renamed", host: " QBT.local ", username: "other-user"), Json);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        stored = await StoredAsync(id);
        Assert.Equal("Moving renamed", stored.Name);
        Assert.Equal("other-user", stored.Username);
        Assert.Equal("placeholder-secret", stored.Password);

        // A new address with the password typed again is accepted.
        var moved = await client.PutAsJsonAsync($"/api/v1/downloadclients/{id}", Form(name: "Moving renamed", host: "elsewhere.local", port: 9090, password: "other-placeholder"), Json);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        stored = await StoredAsync(id);
        Assert.Equal("elsewhere.local", stored.Host);
        Assert.Equal("other-placeholder", stored.Password);

        await client.DeleteAsync($"/api/v1/downloadclients/{id}");
    }

    [Fact]
    public async Task Testing_another_address_with_the_mask_is_refused_and_nothing_is_contacted()
    {
        var client = await ClientAsync();
        await using var elsewhere = await FakeHttpServer.StartAsync();
        var port = new Uri(elsewhere.Url).Port;

        var created = await client.PostAsJsonAsync("/api/v1/downloadclients",
            new DownloadClientRequest("Tested qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Port: 8080, Password: "placeholder-secret", Category: "tested-movies", Priority: 22), Json);
        var id = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!.Id;

        var refused = await client.PostAsJsonAsync($"/api/v1/downloadclients/test?id={id}",
            new DownloadClientRequest("Tested qBittorrent", DownloadClientImplementation.QBittorrent, Host: "127.0.0.1", Port: port, Password: "********", Category: "tested-movies", Priority: 22), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Type the password again", await refused.Content.ReadAsStringAsync());

        // Same host and port, only the scheme moved: still another destination.
        var scheme = await client.PostAsJsonAsync($"/api/v1/downloadclients/test?id={id}",
            new DownloadClientRequest("Tested qBittorrent", DownloadClientImplementation.QBittorrent, Host: "qbt.local", Port: 8080, UseSsl: true, Password: "********", Category: "tested-movies", Priority: 22), Json);
        Assert.Equal(HttpStatusCode.BadRequest, scheme.StatusCode);
        Assert.Contains("Type the password again", await scheme.Content.ReadAsStringAsync());

        Assert.Empty(elsewhere.Requests);
        await client.DeleteAsync($"/api/v1/downloadclients/{id}");
    }

    private async Task<DownloadClient> StoredAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.DownloadClients.AsNoTracking().SingleAsync(c => c.Id == id);
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
