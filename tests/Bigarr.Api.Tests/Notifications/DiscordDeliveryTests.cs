using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Notifications;
using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Notifications;

/// <summary>
/// The Discord format end to end against a fake Discord: validation, the test button, Discord's answers (204, its
/// error body, 429 and its delay), tags, and the webhook token kept out of the logs. Requests to discord.com never
/// leave the machine: the webhook client's handler sends them to the fake.
/// </summary>
public sealed class DiscordDeliveryTests
{
    private const string WebhookPath = "/api/webhooks/123456789/fake-token";
    private const string WebhookUrl = $"https://discord.com{WebhookPath}";
    private const string Token = "fake-token";

    private static readonly Dictionary<string, string> Settings = new() { ["Bigarr:Notifications:SweepSeconds"] = "1" };

    [Fact]
    public async Task Discord_endpoints_are_validated_tested_and_get_embeds_for_their_films()
    {
        await using var discord = await FakeHttpServer.StartAsync();
        discord.Responder = r => r.Path == WebhookPath ? new FakeHttpServer.Answer(204) : null;
        using var factory = new DiscordFactory(discord, Settings);
        var client = await ClientAsync(factory);

        var types = (await client.GetFromJsonAsync<List<NotificationEventTypeResponse>>("/api/v1/notifications/events", Json))!;
        Assert.True(types.Single(t => t.Type == "grabbed").SentToDiscord);
        Assert.True(types.Single(t => t.Type == "downloadFailed").SentToDiscord);
        Assert.False(types.Single(t => t.Type == "versionUpdated").SentToDiscord);

        // The Discord format wants a channel webhook; the other formats refuse one, which is the mistake this format exists for.
        await AssertInvalidAsync(client, Discord("Not discord", "https://example.org/hooks/bigarr"), "discord.com/api/webhooks");
        await AssertInvalidAsync(client, Discord("Plain http", WebhookUrl.Replace("https", "http", StringComparison.Ordinal)), "discord.com/api/webhooks");
        await AssertInvalidAsync(client, new NotificationEndpointRequest("Bigarr body", WebhookUrl), "Pick the Discord format");
        await AssertInvalidAsync(client, new NotificationEndpointRequest("Radarr body", WebhookUrl, Format: WebhookFormat.Radarr), "Pick the Discord format");
        await AssertInvalidAsync(client, Discord("Bookkeeping", WebhookUrl) with { Events = ["versionUpdated"] }, "versionUpdated");
        await AssertInvalidAsync(client, Discord("Bad avatar", WebhookUrl) with { Discord = new DiscordSettings(Avatar: "not a url") }, "avatar");
        var wrongFormatTest = await client.PostAsJsonAsync("/api/v1/notifications/test", new NotificationEndpointRequest("Bigarr body", WebhookUrl), Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrongFormatTest.StatusCode);
        Assert.Empty(discord.Requests);

        // The test button: Discord's 204, the test embed under the chosen name, never a signature.
        var form = Discord("Discord", WebhookUrl) with { Secret = "s3cret", Discord = new DiscordSettings("Bigarr bot") };
        var tested = await client.PostAsJsonAsync("/api/v1/notifications/test", form, Json);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        Assert.Equal(204, (await tested.Content.ReadFromJsonAsync<WebhookTestResponse>(Json))!.StatusCode);
        var test = Assert.Single(discord.Requests);
        Assert.Equal("POST", test.Method);
        Assert.False(test.Headers.ContainsKey(WebhookClient.SignatureHeader));
        using (var doc = JsonDocument.Parse(test.Body))
        {
            Assert.Equal("Bigarr bot", doc.RootElement.GetProperty("username").GetString());
            var embed = doc.RootElement.GetProperty("embeds")[0];
            Assert.Equal("Test message from Bigarr", embed.GetProperty("title").GetString());
            Assert.Equal("Bigarr can reach Discord.", embed.GetProperty("description").GetString());
        }

        // Discord's own words reach the toast.
        discord.Responder = _ => new FakeHttpServer.Answer(400, """{"message": "Cannot send an empty message", "code": 50006}""");
        var refused = await client.PostAsJsonAsync("/api/v1/notifications/test", form, Json);
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("Discord: Cannot send an empty message (50006)", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        discord.Responder = r => r.Path == WebhookPath ? new FakeHttpServer.Answer(204) : null;

        // Saved: Radarr's defaults for the fields left out, no secret, only films tagged kids.
        var kids = (await (await client.PostAsJsonAsync("/api/v1/tags", new TagRequest("kids"))).Content.ReadFromJsonAsync<TagResponse>(Json))!;
        var created = await client.PostAsJsonAsync("/api/v1/notifications", form with { Events = ["movieAdded"], Tags = [kids.Id] }, Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var endpoint = (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!;
        Assert.Equal(WebhookFormat.Discord, endpoint.Format);
        Assert.False(endpoint.HasSecret);
        Assert.Equal("Bigarr bot", endpoint.Discord!.Username);
        Assert.Equal(DiscordSettings.DefaultGrabFields, endpoint.Discord.GrabFields);
        Assert.DoesNotContain(DiscordImportField.CustomFormats, endpoint.Discord.ImportFields!);

        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(factory.ConfigDirectory, "library"));
        await PostMovieAsync(client, new AddMovieRequest(27205, hd, root.Id));
        await PostMovieAsync(client, new AddMovieRequest(603, hd, root.Id, TagIds: [kids.Id]));

        var hit = await discord.WaitForAsync(r => r.Body.Contains("Movie Added", StringComparison.Ordinal));
        using (var doc = JsonDocument.Parse(hit.Body))
        {
            var embed = doc.RootElement.GetProperty("embeds")[0];
            Assert.Equal("The Matrix (1999)", embed.GetProperty("title").GetString());
            Assert.Equal("https://www.themoviedb.org/movie/603", embed.GetProperty("url").GetString());
            Assert.Equal((int)DiscordColor.Success, embed.GetProperty("color").GetInt32());
        }

        var delivered = await WaitUntilAsync(async () =>
        {
            var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpoint.Id}/deliveries", Json))!;
            return list.FirstOrDefault(d => d.State == WebhookDeliveryState.Delivered);
        }, "the Discord delivery to be recorded");
        Assert.Equal(204, delivered.LastStatusCode);
        Assert.Equal(1, delivered.Attempts);
        Assert.DoesNotContain(discord.Requests, r => r.Body.Contains("Inception", StringComparison.Ordinal));
        Assert.Single((await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpoint.Id}/deliveries", Json))!);

        AssertTokenNeverLogged(factory);
    }

    [Fact]
    public async Task A_rate_limited_endpoint_waits_what_Discord_asked_without_spending_attempts()
    {
        await using var discord = await FakeHttpServer.StartAsync();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);

        // One attempt only: a 429 must not count as one, or the first would give the delivery up.
        using var factory = new DiscordFactory(discord, new Dictionary<string, string>(Settings) { ["Bigarr:Notifications:MaxAttempts"] = "1" }, time);
        var client = await ClientAsync(factory);
        var created = await client.PostAsJsonAsync("/api/v1/notifications", Discord("Discord", WebhookUrl), Json);
        var endpoint = (await created.Content.ReadFromJsonAsync<NotificationEndpointResponse>(Json))!;

        discord.Responder = _ => new FakeHttpServer.Answer(429, """{"message": "You are being rate limited.", "retry_after": 30, "global": false}""");
        var (first, second) = await QueueTwoAsync(factory, endpoint.Id, time);

        var limited = await DeliveryAsync(client, endpoint.Id, first, d => d.LastStatusCode == 429, "the rate limit to be recorded");
        Assert.Equal(WebhookDeliveryState.Pending, limited.State);
        Assert.Equal(0, limited.Attempts);
        Assert.Equal("Discord: You are being rate limited.", limited.LastError);
        Assert.Equal(TimeSpan.FromSeconds(30), limited.NextAttemptAt - limited.LastAttemptAt);

        // The other delivery to the same endpoint was not even tried: it waits as long.
        var held = await DeliveryAsync(client, endpoint.Id, second, _ => true, "the second delivery");
        Assert.Null(held.LastAttemptAt);
        Assert.Equal(limited.NextAttemptAt, held.NextAttemptAt);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.Single(discord.Requests);

        // A Retry-After header (Discord's may have a fraction) wins over the body.
        discord.Responder = _ => new FakeHttpServer.Answer(429, Headers: new Dictionary<string, string> { ["Retry-After"] = "2.5" });
        time.Advance(TimeSpan.FromSeconds(31));
        var again = await DeliveryAsync(client, endpoint.Id, first, d => d.LastAttemptAt > limited.LastAttemptAt, "the second rate limit");
        Assert.Equal(0, again.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(2.5), again.NextAttemptAt - again.LastAttemptAt);
        Assert.Equal(2, discord.Requests.Count);

        discord.Responder = _ => new FakeHttpServer.Answer(204);
        time.Advance(TimeSpan.FromSeconds(3));
        var sent = await DeliveryAsync(client, endpoint.Id, second, d => d.State == WebhookDeliveryState.Delivered, "both deliveries to go out");
        Assert.Equal(1, sent.Attempts);
        Assert.Equal(WebhookDeliveryState.Delivered, (await DeliveryAsync(client, endpoint.Id, first, _ => true, "the first delivery")).State);
        Assert.Equal(4, discord.Requests.Count);

        // Any other refusal is a failure as usual, worded by Discord; the health check names the endpoint, not its URL.
        discord.Responder = _ => new FakeHttpServer.Answer(404, """{"message": "Unknown Webhook", "code": 10015}""");
        var (lost, _) = await QueueTwoAsync(factory, endpoint.Id, time);
        var givenUp = await DeliveryAsync(client, endpoint.Id, lost, d => d.State == WebhookDeliveryState.GivenUp, "the delivery to be given up");
        Assert.Equal("Discord: Unknown Webhook (10015)", givenUp.LastError);
        Assert.Equal(404, givenUp.LastStatusCode);

        var health = await client.PostAsync("/api/v1/health/check", null);
        var healthJson = await health.Content.ReadAsStringAsync();
        var issue = Assert.Single(JsonSerializer.Deserialize<HealthResponse>(healthJson, Json)!.Issues, i => i.Source == "notifications");
        Assert.Contains("Webhook Discord is failing", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Unknown Webhook (10015)", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, healthJson, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, await client.GetStringAsync($"/api/v1/notifications/{endpoint.Id}/deliveries"), StringComparison.Ordinal);

        AssertTokenNeverLogged(factory);
    }

    private static NotificationEndpointRequest Discord(string name, string url) => new(name, url, Format: WebhookFormat.Discord);

    private static async Task AssertInvalidAsync(HttpClient client, NotificationEndpointRequest request, string detail)
    {
        var response = await client.PostAsJsonAsync("/api/v1/notifications", request, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(detail, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<MovieResponse> PostMovieAsync(HttpClient client, AddMovieRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }

    /// <summary>Two deliveries of an already dispatched event, due now in the same pass of the sender.</summary>
    private static async Task<(long First, long Second)> QueueTwoAsync(BigarrApiFactory factory, int endpointId, TimeProvider time)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var movie = new MovieRef(1, 603, "tt0133093", "The Matrix", 1999);
        var outboxEvent = new OutboxEvent { Type = "movieAdded", Payload = EventJson.Serialize(new MovieAdded(movie, new VersionRef(1, "1080p", VersionMode.Managed, true, true, "1080p"))), CreatedAt = now, DispatchedAt = now };
        db.Outbox.Add(outboxEvent);
        var deliveries = Enumerable.Range(0, 2).Select(_ => new WebhookDelivery { Event = outboxEvent, EndpointId = endpointId, EventType = "movieAdded", NextAttemptAt = now, CreatedAt = now }).ToList();
        db.WebhookDeliveries.AddRange(deliveries);
        await db.SaveChangesAsync();
        factory.Services.GetRequiredService<WebhookSignal>().Notify();
        return (deliveries[0].Id, deliveries[1].Id);
    }

    private static Task<WebhookDeliveryResponse> DeliveryAsync(HttpClient client, int endpointId, long deliveryId, Func<WebhookDeliveryResponse, bool> until, string what) =>
        WaitUntilAsync(async () =>
        {
            var list = (await client.GetFromJsonAsync<List<WebhookDeliveryResponse>>($"/api/v1/notifications/{endpointId}/deliveries", Json))!;
            return list.FirstOrDefault(d => d.Id == deliveryId && until(d));
        }, what);

    private static void AssertTokenNeverLogged(DiscordFactory factory)
    {
        Assert.NotEmpty(factory.Logs.Lines);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(Token, StringComparison.Ordinal));
    }

    /// <summary>The real app, its webhook client wired to the fake Discord, every log line kept down to Trace.</summary>
    private sealed class DiscordFactory(FakeHttpServer discord, IReadOnlyDictionary<string, string> settings, TimeProvider? time = null)
        : BigarrApiFactory(null, settings, deleteOnDispose: true, time)
    {
        public LogCapture Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(Logs);
                logging.AddFilter<LogCapture>(null, LogLevel.Trace);
            });
            builder.ConfigureTestServices(services => services.AddHttpClient(WebhookClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new ToFakeDiscord(new Uri(discord.Url))));
        }
    }

    /// <summary>Sends what is addressed to Discord to the fake instead, path and query intact.</summary>
    private sealed class ToFakeDiscord(Uri fake) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri && DiscordWebhookUrl.IsDiscordHost(uri))
            {
                request.RequestUri = new UriBuilder(uri) { Scheme = fake.Scheme, Host = fake.Host, Port = fake.Port }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Lines.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
