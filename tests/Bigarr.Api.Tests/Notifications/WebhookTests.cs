using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Notifications;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>Outbox and webhooks: events leave the process signed, are retried, and survive a restart.</summary>
public sealed class WebhookTests
{
    [Fact]
    public async Task Endpoints_are_managed_tested_and_receive_the_events_they_subscribe_to()
    {
        await using var sink = await WebhookSink.StartAsync();
        using var factory = new ConfiguredBigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Notifications:SweepSeconds"] = "1" });
        var client = await ClientAsync(factory);

        var types = (await client.GetFromJsonAsync<List<NotificationEventTypeResponse>>("/api/v1/notifications/events", Json))!;
        Assert.Contains(types, t => t.Type == "grabbed");
        Assert.Contains(types, t => t.Type == "imported");

        // Validation.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("", sink.Url), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Bad url", "not a url"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Bad event", sink.Url, Events: ["nope"]), Json)).StatusCode);

        // The test button before saving: a real POST, signed.
        var tested = await client.PostAsJsonAsync("/api/v1/notifications/test", new NotificationEndpointRequest("Sink", sink.Url, Secret: "s3cret"), Json);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        Assert.Equal(200, (await tested.Content.ReadFromJsonAsync<WebhookTestResponse>(Json))!.StatusCode);
        var testHit = await sink.WaitForAsync(r => r.EventType == "test");
        Assert.Equal("test", testHit.DeliveryId);
        Assert.Equal(WebhookClient.Sign("s3cret", testHit.Body), testHit.Signature);
        using (var doc = JsonDocument.Parse(testHit.Body))
        {
            Assert.Equal("test", doc.RootElement.GetProperty("eventType").GetString());
            Assert.Equal("Bigarr", doc.RootElement.GetProperty("instance").GetProperty("name").GetString());
            Assert.Contains("Sink", doc.RootElement.GetProperty("data").GetProperty("message").GetString(), StringComparison.Ordinal);
        }

        sink.StatusCode = 503;
        var refused = await client.PostAsJsonAsync("/api/v1/notifications/test", new NotificationEndpointRequest("Sink", sink.Url), Json);
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("503", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        sink.StatusCode = 200;

        // Saved, subscribed to grabs only.
        var created = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Sink", sink.Url, Secret: "s3cret", Events: ["grabbed", "test"]), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var endpoint = (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!;
        Assert.True(endpoint.HasSecret);
        Assert.Equal(["grabbed", "test"], endpoint.Events);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Sink", sink.Url), Json)).StatusCode);

        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-hook-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id); // movieAdded: not subscribed
        var indexer = await AddMockIndexerAsync(client, "Mock hook");
        var downloadClient = await AddMockClientAsync(client, "Mock hook qbt");
        try
        {
            var search = await SearchAsync(client, movie.Id);
            var release = search.Releases.First(r => r.Versions.Any(v => v.Approved));
            var grab = await GrabAsync(client, release, movie.Versions.Single().Id);

            var hit = await sink.WaitForAsync(r => r.EventType == "grabbed");
            Assert.Equal(WebhookClient.Sign("s3cret", hit.Body), hit.Signature);
            using (var doc = JsonDocument.Parse(hit.Body))
            {
                var data = doc.RootElement.GetProperty("data");
                Assert.Equal("The Matrix", data.GetProperty("movie").GetProperty("title").GetString());
                Assert.Equal(603, data.GetProperty("movie").GetProperty("tmdbId").GetInt32());
                Assert.Equal(release.Title, data.GetProperty("release").GetProperty("title").GetString());
                Assert.Equal(grab.GrabId, data.GetProperty("grabId").GetInt32());
                Assert.Equal("interactive", data.GetProperty("trigger").GetString());
                Assert.Equal("Mock hook qbt", data.GetProperty("downloadClient").GetString());
            }

            Assert.DoesNotContain(sink.Requests, r => r.EventType == "movieAdded");

            var deliveries = await WaitUntilAsync(async () =>
            {
                var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpoint.Id}/deliveries", Json))!;
                return list.All(d => d.State == WebhookDeliveryState.Delivered) ? list : null;
            }, "the delivery to be recorded");
            var delivery = Assert.Single(deliveries);
            Assert.Equal("grabbed", delivery.EventType);
            Assert.Equal(1, delivery.Attempts);
            Assert.Equal(200, delivery.LastStatusCode);
            Assert.Equal(delivery.Id.ToString(), hit.DeliveryId);

            var listed = Assert.Single((await client.GetFromJsonAsync<List<NotificationEndpointResponse>>("/api/v1/notifications", Json))!);
            Assert.Equal(0, listed.PendingDeliveries);
            Assert.NotNull(listed.LastDeliveredAt);

            // Retrying a delivered one is refused; a disabled endpoint stops receiving.
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/notifications/deliveries/{delivery.Id}/retry", null)).StatusCode);
            var updated = await client.PutAsJsonAsync($"/api/v1/notifications/{endpoint.Id}", new NotificationEndpointRequest("Sink", sink.Url, Enabled: false, Events: ["grabbed"]), Json);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.False((await updated.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!.HasSecret);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/notifications/{endpoint.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/notifications/{endpoint.Id}")).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task A_failed_delivery_is_retried_after_a_restart()
    {
        await using var sink = await WebhookSink.StartAsync();
        var configDirectory = Path.Combine(Path.GetTempPath(), $"bigarr-hook-restart-{Guid.NewGuid():N}");
        var settings = new Dictionary<string, string> { ["Bigarr:Notifications:SweepSeconds"] = "1" };
        long deliveryId;
        int endpointId;

        // First life: the receiver is down, the delivery fails and waits for its retry.
        using (var first = new ConfiguredBigarrApiFactory(configDirectory, settings, deleteOnDispose: false))
        {
            var client = await ClientAsync(first);
            sink.StatusCode = 500;
            var created = await client.PostAsJsonAsync("/api/v1/notifications", new NotificationEndpointRequest("Flaky", sink.Url, Events: ["movieAdded"]), Json);
            endpointId = (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!.Id;

            var (hd, _) = await ProfilesAsync(client);
            var root = await AddRootAsync(client, Path.Combine(configDirectory, "library"));
            await AddMovieAsync(client, 603, hd, root.Id);

            await sink.WaitForAsync(r => r.EventType == "movieAdded");
            var pending = await WaitUntilAsync(async () =>
            {
                var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpointId}/deliveries", Json))!;
                return list.FirstOrDefault(d => d.Attempts >= 1);
            }, "the failed attempt to be recorded");
            Assert.Equal(WebhookDeliveryState.Pending, pending.State);
            Assert.Equal(500, pending.LastStatusCode);
            Assert.NotNull(pending.NextAttemptAt);
            Assert.True(pending.NextAttemptAt > DateTime.UtcNow.AddSeconds(30)); // the first retry waits a minute
            deliveryId = pending.Id;

            var listed = Assert.Single((await client.GetFromJsonAsync<List<NotificationEndpointResponse>>("/api/v1/notifications", Json))!);
            Assert.Equal(1, listed.PendingDeliveries);
            Assert.Contains("500", listed.LastError, StringComparison.Ordinal);
        }

        // Second life on the same database: the receiver is back, the pending delivery is replayed at startup.
        sink.StatusCode = 200;
        var before = sink.Requests.Count;
        using (var second = new ConfiguredBigarrApiFactory(configDirectory, settings, deleteOnDispose: true))
        {
            var client = await ClientAsync(second);
            var delivered = await WaitUntilAsync(async () =>
            {
                var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpointId}/deliveries", Json))!;
                return list.FirstOrDefault(d => d.Id == deliveryId && d.State == WebhookDeliveryState.Delivered);
            }, "the replayed delivery");
            Assert.Equal(2, delivered.Attempts);
            Assert.Null(delivered.LastError);
            Assert.True(sink.Requests.Count > before);
            var replay = sink.Requests.Last(r => r.EventType == "movieAdded");
            Assert.Equal(deliveryId.ToString(), replay.DeliveryId);
            Assert.Null(replay.Signature); // no secret on this endpoint

            // Manual retry of a given-up delivery: reset and sent again.
            sink.StatusCode = 500;
            var root = (await client.GetFromJsonAsync<List<Bigarr.Api.Features.Library.RootFolderResponse>>("/api/v1/rootfolders", Json))!.Single();
            var (hd, _) = await ProfilesAsync(client);
            await AddMovieAsync(client, 27205, hd, root.Id);
            var failing = await WaitUntilAsync(async () =>
            {
                var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpointId}/deliveries", Json))!;
                return list.FirstOrDefault(d => d.Id != deliveryId && d.Attempts >= 1);
            }, "the second failed attempt");
            sink.StatusCode = 200;
            var retried = await client.PostAsync($"/api/v1/notifications/deliveries/{failing.Id}/retry", null);
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
            Assert.Equal(0, (await retried.Content.ReadFromJsonAsync<WebhookDeliveryResponse>(Json))!.Attempts);
            var done = await WaitUntilAsync(async () =>
            {
                var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpointId}/deliveries", Json))!;
                return list.FirstOrDefault(d => d.Id == failing.Id && d.State == WebhookDeliveryState.Delivered);
            }, "the manual retry");
            Assert.Equal(1, done.Attempts);
        }
    }
}
