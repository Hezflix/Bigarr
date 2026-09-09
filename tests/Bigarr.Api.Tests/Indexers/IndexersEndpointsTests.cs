using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Tests.Indexers;

public sealed class IndexersEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Indexers_are_created_listed_updated_and_deleted()
    {
        var client = await ClientAsync();

        var created = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest("Mock one", IndexerImplementation.Mock, Priority: 10), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var indexer = (await created.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        Assert.Equal("Mock one", indexer.Name);
        Assert.Equal(IndexerImplementation.Mock, indexer.Implementation);
        Assert.Null(indexer.BaseUrl);
        Assert.Equal("/api", indexer.ApiPath);
        Assert.Equal(Indexer.DefaultCategories, indexer.Categories);
        Assert.Equal(10, indexer.Priority);
        Assert.True(indexer.EnableInteractiveSearch);
        Assert.True(indexer.Status.Healthy);
        Assert.Equal(1, indexer.MinimumSeeders);

        var raw = await client.GetStringAsync($"/api/v1/indexers/{indexer.Id}");
        Assert.Contains("\"implementation\":\"mock\"", raw);
        Assert.Contains("\"protocol\":\"torrent\"", raw);

        var duplicate = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest("Mock one", IndexerImplementation.Mock), Json);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var updated = await client.PutAsJsonAsync($"/api/v1/indexers/{indexer.Id}",
            new IndexerRequest("Mock renamed", IndexerImplementation.Mock, Categories: [2040, 2045, 2040], Priority: 1, EnableRss: false, MinimumSeeders: 3, RateLimitSeconds: 0), Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var renamed = (await updated.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        Assert.Equal("Mock renamed", renamed.Name);
        Assert.Equal([2040, 2045], renamed.Categories);
        Assert.False(renamed.EnableRss);
        Assert.Equal(3, renamed.MinimumSeeders);
        Assert.Equal(0, renamed.RateLimitSeconds);

        var list = (await client.GetFromJsonAsync<List<IndexerResponse>>("/api/v1/indexers", Json))!;
        Assert.Contains(list, i => i.Id == indexer.Id && i.Name == "Mock renamed");

        var deleted = await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/indexers/{indexer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}")).StatusCode);
    }

    [Fact]
    public async Task Torznab_indexers_need_a_valid_url_and_sane_numbers()
    {
        var client = await ClientAsync();

        async Task<string> Refused(IndexerRequest request)
        {
            var response = await client.PostAsJsonAsync("/api/v1/indexers", request, Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("Invalid URL", await Refused(new IndexerRequest("Prowlarr", IndexerImplementation.Torznab)));
        Assert.Contains("Invalid URL", await Refused(new IndexerRequest("Prowlarr", IndexerImplementation.Torznab, BaseUrl: "prowlarr:9696")));
        Assert.Contains("Invalid URL", await Refused(new IndexerRequest("Prowlarr", IndexerImplementation.Torznab, BaseUrl: "ftp://prowlarr:9696")));
        Assert.Contains("Name is required", await Refused(new IndexerRequest(" ", IndexerImplementation.Mock)));
        Assert.Contains("Invalid priority", await Refused(new IndexerRequest("P", IndexerImplementation.Mock, Priority: 0)));
        Assert.Contains("Invalid priority", await Refused(new IndexerRequest("P", IndexerImplementation.Mock, Priority: 51)));
        Assert.Contains("Invalid minimum seeders", await Refused(new IndexerRequest("P", IndexerImplementation.Mock, MinimumSeeders: -1)));
        Assert.Contains("Invalid rate limit", await Refused(new IndexerRequest("P", IndexerImplementation.Mock, RateLimitSeconds: 4000)));
        Assert.Contains("Invalid categories", await Refused(new IndexerRequest("P", IndexerImplementation.Mock, Categories: [2000, -5])));

        var ok = await client.PostAsJsonAsync("/api/v1/indexers",
            new IndexerRequest("Prowlarr", IndexerImplementation.Torznab, BaseUrl: "http://prowlarr:9696/1/", ApiPath: "api/", ApiKey: " secret "), Json);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var indexer = (await ok.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        Assert.Equal("http://prowlarr:9696/1", indexer.BaseUrl);
        Assert.Equal("/api", indexer.ApiPath);
        Assert.Equal("secret", indexer.ApiKey);

        await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
    }

    [Fact]
    public async Task Testing_an_indexer_reports_its_capabilities_or_the_failure()
    {
        var client = await ClientAsync();

        var unsaved = await client.PostAsJsonAsync("/api/v1/indexers/test", new IndexerRequest("Mock", IndexerImplementation.Mock), Json);
        Assert.Equal(HttpStatusCode.OK, unsaved.StatusCode);
        var result = (await unsaved.Content.ReadFromJsonAsync<IndexerTestResponse>(Json))!;
        Assert.True(result.SupportsMovieSearch);
        Assert.True(result.SupportsTmdbSearch);
        Assert.True(result.SupportsImdbSearch);
        Assert.Equal(Indexer.DefaultCategories.Length, result.CategoryCount);
        Assert.True(result.RecentReleases > 0);

        var created = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest("Mock to test", IndexerImplementation.Mock), Json);
        var saved = (await created.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        var savedTest = await client.PostAsync($"/api/v1/indexers/{saved.Id}/test", null);
        Assert.Equal(HttpStatusCode.OK, savedTest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/indexers/999999/test", null)).StatusCode);

        // Nothing listens on this port: the test fails fast with a typed message instead of a 500.
        var unreachable = await client.PostAsJsonAsync("/api/v1/indexers/test",
            new IndexerRequest("Dead", IndexerImplementation.Torznab, BaseUrl: "http://127.0.0.1:1", ApiKey: "x"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, unreachable.StatusCode);
        var problem = await unreachable.Content.ReadAsStringAsync();
        Assert.Contains("Indexer test failed", problem);
        Assert.Contains("Could not reach the indexer", problem);

        await client.DeleteAsync($"/api/v1/indexers/{saved.Id}");
    }

    [Fact]
    public async Task Indexers_require_authentication()
    {
        var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/indexers")).StatusCode);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}
