using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Tests.Auth;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>§7.4: Prowlarr reads the schema, pushes Torznab indexers pointing at itself, and recognises them by key and URL.</summary>
public sealed class ProwlarrPushTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Prowlarr_can_push_update_and_remove_torznab_indexers()
    {
        var session = await SessionAsync(factory);
        var (prowlarr, _) = await KeyClientAsync(factory, session, "prowlarr", "default", header: true);

        var schemaResponse = await prowlarr.GetAsync("/api/v3/indexer/schema");
        Assert.Equal(HttpStatusCode.OK, schemaResponse.StatusCode);
        Assert.Equal(RadarrCompat.ApplicationVersion, schemaResponse.Headers.GetValues(RadarrCompat.VersionHeader).Single());
        var schema = await schemaResponse.ReadJsonAsync();
        var torznab = schema.EnumerateArray().Single(s => s.GetProperty("implementation").GetString() == "Torznab");
        var newznab = schema.EnumerateArray().Single(s => s.GetProperty("implementation").GetString() == "Newznab");
        Assert.Equal("TorznabSettings", torznab.GetProperty("configContract").GetString());
        Assert.Equal("NewznabSettings", newznab.GetProperty("configContract").GetString());
        var fieldNames = torznab.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.All(new[] { "baseUrl", "apiPath", "apiKey", "categories", "minimumSeeders", "seedCriteria.seedRatio", "seedCriteria.seedTime", "rejectBlocklistedTorrentHashesWhileGrabbing" }, name => Assert.Contains(name, fieldNames));

        // What Prowlarr builds from the schema for one of its indexers.
        var body = new
        {
            id = 0,
            name = "Nyaa.si (Prowlarr)",
            implementation = "Torznab",
            configContract = "TorznabSettings",
            enableRss = true,
            enableAutomaticSearch = true,
            enableInteractiveSearch = false,
            priority = 20,
            downloadClientId = 0,
            tags = Array.Empty<int>(),
            fields = new object[]
            {
                new { name = "baseUrl", value = "http://prowlarr:9696/7/" },
                new { name = "apiPath", value = "/api" },
                new { name = "apiKey", value = "prowlarr-key" },
                new { name = "categories", value = new[] { 2000, 2010, 2040 } },
                new { name = "minimumSeeders", value = 2 },
                new { name = "seedCriteria.seedRatio", value = 1.5 },
                new { name = "seedCriteria.seedTime", value = 60 },
                new { name = "rejectBlocklistedTorrentHashesWhileGrabbing", value = false },
            },
        };
        var created = await prowlarr.PostAsJsonAsync("/api/v3/indexer", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resource = await created.ReadJsonAsync();
        var id = resource.GetProperty("id").GetInt32();
        Assert.True(id > 0);

        var stored = (await session.GetFromJsonAsync<IndexerResponse>($"/api/v1/indexers/{id}", Json))!;
        Assert.Equal("Nyaa.si (Prowlarr)", stored.Name);
        Assert.Equal("http://prowlarr:9696/7", stored.BaseUrl);
        Assert.Equal("prowlarr-key", stored.ApiKey);
        Assert.Equal([2000, 2010, 2040], stored.Categories);
        Assert.Equal(2, stored.MinimumSeeders);
        Assert.Equal(1.5, stored.SeedRatio);
        Assert.Equal(60, stored.SeedTimeMinutes);
        Assert.Equal(20, stored.Priority);
        Assert.False(stored.EnableInteractiveSearch);
        Assert.Equal(7, stored.ProwlarrId);

        // Prowlarr reads them back and matches on apiKey and the /{id}/ in baseUrl.
        var list = await (await prowlarr.GetAsync("/api/v3/indexer")).ReadJsonAsync();
        var mine = list.EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == id);
        var fields = mine.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("name").GetString()!, f => f);
        Assert.Equal("http://prowlarr:9696/7/", fields["baseUrl"].GetProperty("value").GetString());
        Assert.Equal("prowlarr-key", fields["apiKey"].GetProperty("value").GetString());
        Assert.Equal(2, fields["minimumSeeders"].GetProperty("value").GetInt32());
        Assert.Equal(1.5, fields["seedCriteria.seedRatio"].GetProperty("value").GetDouble());
        Assert.Equal([2000, 2010, 2040], fields["categories"].GetProperty("value").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await prowlarr.GetAsync($"/api/v3/indexer/{id}")).StatusCode);

        var updated = await prowlarr.PutAsJsonAsync($"/api/v3/indexer/{id}?forceSave=true", new
        {
            body.id, body.name, body.implementation, body.configContract, enableRss = false, body.enableAutomaticSearch, body.enableInteractiveSearch, priority = 5, body.downloadClientId, body.tags, body.fields,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        stored = (await session.GetFromJsonAsync<IndexerResponse>($"/api/v1/indexers/{id}", Json))!;
        Assert.False(stored.EnableRss);
        Assert.Equal(5, stored.Priority);

        // Usenet is refused with Radarr's validation shape; Prowlarr retries with forceSave and gets the same answer.
        var usenet = await prowlarr.PostAsJsonAsync("/api/v3/indexer?forceSave=true", new { body.id, name = "NZBgeek (Prowlarr)", implementation = "Newznab", configContract = "NewznabSettings", body.fields });
        Assert.Equal(HttpStatusCode.BadRequest, usenet.StatusCode);
        Assert.Equal("Implementation", (await usenet.ReadJsonAsync())[0].GetProperty("propertyName").GetString());

        // The application test posts a Newznab indexer aimed at Prowlarr's aggregate endpoint; unreachable here → 400, header still there.
        var test = await prowlarr.PostAsJsonAsync("/api/v3/indexer/test", new { body.id, name = "Test", implementation = "Newznab", configContract = "NewznabSettings", fields = new object[] { new { name = "baseUrl", value = "http://127.0.0.1:1/0/" }, new { name = "apiPath", value = "/api" }, new { name = "apiKey", value = "k" }, new { name = "categories", value = new[] { 2000 } } } });
        Assert.Equal(HttpStatusCode.BadRequest, test.StatusCode);
        Assert.Equal(RadarrCompat.ApplicationVersion, test.Headers.GetValues(RadarrCompat.VersionHeader).Single());

        Assert.Equal(HttpStatusCode.OK, (await prowlarr.DeleteAsync($"/api/v3/indexer/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prowlarr.GetAsync($"/api/v3/indexer/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prowlarr.DeleteAsync($"/api/v3/indexer/{id}")).StatusCode);
    }
}
