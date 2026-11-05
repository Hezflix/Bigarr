using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>Tags and the Radarr format end to end: what reaches which endpoint, and in which shape.</summary>
public sealed class WebhookFilteringTests
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Bigarr:Notifications:SweepSeconds"] = "1",
        ["Bigarr:Notifications:ApplicationUrl"] = "https://bigarr.example.org",
    };

    [Fact]
    public async Task A_tagged_endpoint_hears_only_about_films_with_its_tags_and_always_about_health()
    {
        await using var tagged = await WebhookSink.StartAsync();
        await using var everything = await WebhookSink.StartAsync();
        using var factory = new ConfiguredBigarrApiFactory(null, Settings);
        var client = await ClientAsync(factory);

        var kids = (await (await client.PostAsJsonAsync("/api/v1/tags", new TagRequest("kids"))).Content.ReadFromJsonAsync<TagResponse>(Json))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Ghost tag", tagged.Url, Tags: [9999]), Json)).StatusCode);

        var created = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Kids", tagged.Url, Events: ["movieAdded", "healthIssue"], Tags: [kids.Id]), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var endpoint = (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!;
        Assert.Equal([kids.Id], endpoint.Tags);
        Assert.Equal(WebhookFormat.Bigarr, endpoint.Format);
        Assert.Equal("default", endpoint.View);
        await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Everything", everything.Url, Events: ["movieAdded"]), Json);

        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(factory.ConfigDirectory, "library"));
        var matrix = await PostMovieAsync(client, new AddMovieRequest(603, hd, root.Id, TagIds: [kids.Id]));
        var inception = await PostMovieAsync(client, new AddMovieRequest(27205, hd, root.Id));

        await everything.WaitForAsync(r => r.EventType == "movieAdded" && r.Body.Contains("\"tmdbId\":27205", StringComparison.Ordinal));
        var hit = await tagged.WaitForAsync(r => r.EventType == "movieAdded");
        using (var doc = JsonDocument.Parse(hit.Body))
        {
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(603, data.GetProperty("movie").GetProperty("tmdbId").GetInt32());
            Assert.Equal("kids", data.GetProperty("version").GetProperty("tags")[0].GetProperty("label").GetString());
            Assert.Equal(matrix.Versions.Single().Path, data.GetProperty("version").GetProperty("folder").GetString());
            Assert.Equal("https://bigarr.example.org", doc.RootElement.GetProperty("instance").GetProperty("applicationUrl").GetString());
        }

        // Both films were dispatched in the same pass: the tagged endpoint got a delivery for one of them only.
        var deliveries = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpoint.Id}/deliveries", Json))!;
        Assert.Single(deliveries, d => d.EventType == "movieAdded");
        Assert.DoesNotContain(tagged.Requests, r => r.Body.Contains("\"tmdbId\":27205", StringComparison.Ordinal));

        // No indexer, no client: the health check raises issues, which no tag filters.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/system/tasks/healthCheck/run", null)).StatusCode);
        await tagged.WaitForAsync(r => r.EventType == "healthIssue");

        await client.DeleteAsync($"/api/v1/movies/{matrix.Id}");
        await client.DeleteAsync($"/api/v1/movies/{inception.Id}");
    }

    [Fact]
    public async Task A_Radarr_format_endpoint_sends_Radarr_payloads_about_the_versions_of_its_view()
    {
        await using var standard = await WebhookSink.StartAsync();
        await using var uhd = await WebhookSink.StartAsync();
        using var factory = new ConfiguredBigarrApiFactory(null, Settings);
        var client = await ClientAsync(factory);

        // Events Radarr has no word for cannot be subscribed to in its format; a view is a view.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Bad", standard.Url, Events: ["versionAdded"], Format: WebhookFormat.Radarr), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Bad", standard.Url, Format: WebhookFormat.Radarr, View: "version:Not A Slug"), Json)).StatusCode);

        // The test button sends Radarr's own test payload.
        var tested = await client.PostAsJsonAsync("/api/v1/notifications/test", new NotificationEndpointRequest("Standard", standard.Url, Format: WebhookFormat.Radarr), Json);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        using (var doc = JsonDocument.Parse((await standard.WaitForAsync(r => r.EventType == "test")).Body))
        {
            Assert.Equal("Test", doc.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("Test Title", doc.RootElement.GetProperty("movie").GetProperty("title").GetString());
            Assert.Equal("https://bigarr.example.org", doc.RootElement.GetProperty("applicationUrl").GetString());
        }

        var created = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Standard", standard.Url, Format: WebhookFormat.Radarr), Json);
        Assert.Equal(WebhookFormat.Radarr, (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!.Format);
        var uhdCreated = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("4K", uhd.Url, Format: WebhookFormat.Radarr, View: "version:4k"), Json);
        Assert.Equal("version:4k", (await uhdCreated.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!.View);

        var (hd, uhdProfile) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(factory.ConfigDirectory, "library"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var standardVersion = movie.Versions.Single();
        var uhdVersion = await AddVersionAsync(client, movie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhdProfile));
        var indexer = await AddMockIndexerAsync(client, "Mock radarr hook");
        var downloadClient = await AddMockClientAsync(client, "Mock radarr qbt");
        var release = (await SearchAsync(client, movie.Id)).Releases.First();
        var grab = await GrabAsync(client, release, uhdVersion.Id);

        // The film was added as its 1080p version: a Radarr movie for the default view only.
        var added = await standard.WaitForAsync(r => r.EventType == "movieAdded");
        using (var doc = JsonDocument.Parse(added.Body))
        {
            Assert.Equal("MovieAdded", doc.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(standardVersion.Id, doc.RootElement.GetProperty("movie").GetProperty("id").GetInt32());
            Assert.Equal("manual", doc.RootElement.GetProperty("addMethod").GetString());
        }

        // The grab was for the 4k version: only the 4k view hears about it, with the compat id of that version.
        var grabbed = await uhd.WaitForAsync(r => r.EventType == "grabbed");
        int compatId;
        using (var doc = JsonDocument.Parse(grabbed.Body))
        {
            var body = doc.RootElement;
            Assert.Equal("Grab", body.GetProperty("eventType").GetString());
            Assert.Equal("Bigarr", body.GetProperty("instanceName").GetString());
            compatId = body.GetProperty("movie").GetProperty("id").GetInt32();
            Assert.Equal(uhdVersion.Id, compatId);
            Assert.Equal(603, body.GetProperty("remoteMovie").GetProperty("tmdbId").GetInt32());
            Assert.Equal(release.Title, body.GetProperty("release").GetProperty("releaseTitle").GetString());
            Assert.Equal("Mock radarr qbt", body.GetProperty("downloadClient").GetString());
            Assert.Equal("Mock", body.GetProperty("downloadClientType").GetString());
            Assert.Equal(grab.DownloadId, body.GetProperty("downloadId").GetString());
            Assert.False(body.TryGetProperty("data", out _));
        }

        // Same id, same film on the v3 API with a key of the same view.
        var (v3, _) = await CompatTestHelpers.KeyClientAsync(factory, client, "4K Seerr", "version:4k");
        using (var doc = JsonDocument.Parse(await v3.GetStringAsync($"/api/v3/movie/{compatId}")))
        {
            Assert.Equal(603, doc.RootElement.GetProperty("tmdbId").GetInt32());
        }

        Assert.DoesNotContain(standard.Requests, r => r.EventType == "grabbed");
        Assert.DoesNotContain(standard.Requests.Concat(uhd.Requests), r => r.EventType == "versionAdded");
        Assert.DoesNotContain(uhd.Requests, r => r.EventType == "movieAdded");

        await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
    }

    private static async Task<MovieResponse> PostMovieAsync(HttpClient client, AddMovieRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }
}
