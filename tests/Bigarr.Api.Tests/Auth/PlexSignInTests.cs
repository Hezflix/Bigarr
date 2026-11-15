using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// Signing in with Plex against a fake plex.tv: the admin links their Plex account from Settings, then "Sign in with
/// Plex" opens a session; an account linked to no one is refused; the password keeps working whatever plex.tv does;
/// the cookie and the API keys stay two separate surfaces.
/// </summary>
public sealed class PlexSignInTests
{
    [Fact]
    public async Task The_admin_links_their_plex_account_then_signs_in_with_it()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);

        // Off and unlinked: the login page offers nothing.
        Assert.Empty(await ProvidersAsync(factory.CreateAnonymousClient()));
        var settings = await SettingsAsync(session);
        Assert.False(settings.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("link").ValueKind);

        await EnableAsync(session, true);
        Assert.Empty(await ProvidersAsync(factory.CreateAnonymousClient())); // on, but linked to no one yet

        var link = await StartAsync(session, "/api/v1/auth/external/plex/link");
        var authUrl = link.GetProperty("authUrl").GetString()!;
        var clientIdentifier = StoredClientIdentifier(factory);
        Assert.StartsWith("https://app.plex.tv/auth/#!?", authUrl, StringComparison.Ordinal);
        Assert.Contains("clientID=" + clientIdentifier, authUrl, StringComparison.Ordinal);
        Assert.Contains("code=code" + plexTv.LastPinId, authUrl, StringComparison.Ordinal);

        Assert.Equal("pending", await PollAsync(session, link));
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        var linked = await PollResponseAsync(session, link);
        Assert.Equal("linked", linked.GetProperty("status").GetString());
        Assert.Equal("demo-owner", linked.GetProperty("account").GetString());

        settings = await SettingsAsync(session);
        Assert.Equal("1001", settings.GetProperty("link").GetProperty("externalId").GetString());
        Assert.Equal("demo-owner", settings.GetProperty("link").GetProperty("externalUsername").GetString());
        Assert.Equal("owner@example.invalid", settings.GetProperty("link").GetProperty("email").GetString());
        Assert.Single(await ProvidersAsync(factory.CreateAnonymousClient()));

        // The login page: another browser, no session.
        var browser = factory.CreateClientWithCookies();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
        var login = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        Assert.Equal("pending", await PollAsync(browser, login));
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        Assert.Equal("signedIn", await PollAsync(browser, login));

        var me = await browser.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        Assert.Equal(BigarrApiFactory.AdminUsername, me!.Username);
        Assert.Equal("Session", me.AuthKind);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/system/status")).StatusCode);
        // A cookie opened through Plex is a cookie like any other: it never opens /radarr/api/v3.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/radarr/api/v3/system/status")).StatusCode);

        // The flow is over: polling it again tells nothing more.
        Assert.Equal("expired", await PollAsync(browser, login));

        Assert.DoesNotContain(plexTv.Requests, r => r.Path == "/api/v2/user" && !r.Headers.ContainsKey("X-Plex-Token"));
        Assert.Equal([clientIdentifier], plexTv.ClientIdentifiers); // one identifier for every call
        Assert.Contains("demo-owner", factory.Logs, StringComparison.Ordinal); // the capture works
        Assert.DoesNotContain("owner@example.invalid", factory.Logs, StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var row = Assert.Single(db.ExternalLogins);
        Assert.NotNull(row.LastLoginAt);
        Assert.NotNull(db.Users.Single().LastLoginAt);
    }

    [Fact]
    public async Task A_plex_account_linked_to_no_one_is_refused()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        await EnableAsync(session, true);

        var browser = factory.CreateClientWithCookies();
        var login = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Stranger);
        var refused = await PollResponseAsync(browser, login);

        Assert.Equal("unknownAccount", refused.GetProperty("status").GetString());
        Assert.Equal("demo-stranger", refused.GetProperty("account").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Single(db.Users); // no user is created
    }

    [Fact]
    public async Task A_pin_plex_tv_forgot_or_a_flow_past_its_time_ends_as_expired()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new Factory(plexTv.Url, clock);
        var session = await CompatTestHelpers.SessionAsync(factory);
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        await EnableAsync(session, true);

        var browser = factory.CreateClientWithCookies();
        var forgotten = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        plexTv.Expire(plexTv.LastPinId);
        Assert.Equal("expired", await PollAsync(browser, forgotten));

        // plex.tv says its PIN lives until 2099; Bigarr still gives up after 15 minutes.
        var slow = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        Assert.Equal("pending", await PollAsync(browser, slow));
        clock.Advance(TimeSpan.FromMinutes(16));
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        Assert.Equal("expired", await PollAsync(browser, slow));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_token_plex_tv_will_not_vouch_for_is_denied_and_a_cancelled_flow_cannot_finish()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        await EnableAsync(session, true);

        var browser = factory.CreateClientWithCookies();
        var denied = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        plexTv.RefuseTokens = true;
        Assert.Equal("denied", await PollAsync(browser, denied));
        plexTv.RefuseTokens = false;

        // The popup was closed: the page cancels, a late approval no longer signs in.
        var cancelled = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        var cancel = await browser.PostAsJsonAsync("/api/v1/auth/external/cancel", new { flowId = cancelled.GetProperty("flowId").GetString() });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        Assert.Equal("expired", await PollAsync(browser, cancelled));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Only_the_browser_that_started_a_sign_in_can_finish_it()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        await EnableAsync(session, true);

        var victim = factory.CreateClientWithCookies();
        var login = await StartAsync(victim, "/api/v1/auth/external/plex/login");
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);

        // Someone who learnt the flow id but does not hold the browser's cookie gets nothing.
        var other = factory.CreateClientWithCookies();
        Assert.Equal("expired", await PollAsync(other, login));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal("signedIn", await PollAsync(victim, login));
    }

    [Fact]
    public async Task Unlinking_or_turning_plex_off_stops_the_sign_in_and_the_password_always_works()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        await EnableAsync(session, true);

        // Turned off while someone is signing in: the flow ends without a session.
        var browser = factory.CreateClientWithCookies();
        var login = await StartAsync(browser, "/api/v1/auth/external/plex/login");
        await EnableAsync(session, false);
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        Assert.Equal("disabled", await PollAsync(browser, login));
        Assert.Empty(await ProvidersAsync(browser));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync("/api/v1/auth/external/plex/login", new { })).StatusCode);

        await EnableAsync(session, true);
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync("/api/v1/auth/external/plex/link")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await SettingsAsync(session)).GetProperty("link").ValueKind);
        Assert.Empty(await ProvidersAsync(browser));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsJsonAsync("/api/v1/auth/external/plex/login", new { })).StatusCode);

        // Plex on, linked again, plex.tv unreachable: the button answers 502, the password still signs in.
        await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
        plexTv.Down = true;
        var unreachable = await browser.PostAsJsonAsync("/api/v1/auth/external/plex/login", new { });
        Assert.Equal(HttpStatusCode.BadGateway, unreachable.StatusCode);
        var local = await factory.CreateClientWithCookies().PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, local.StatusCode);
    }

    [Fact]
    public async Task A_plex_account_already_linked_to_another_user_is_not_linked_again()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var other = db.Users.Add(new User { Username = "someone-else", PasswordHash = "x" }).Entity;
            await db.SaveChangesAsync();
            db.ExternalLogins.Add(new ExternalLogin { Provider = "plex", ExternalId = "1001", UserId = other.Id });
            await db.SaveChangesAsync();
        }

        var link = await StartAsync(session, "/api/v1/auth/external/plex/link");
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        Assert.Equal("linkedElsewhere", await PollAsync(session, link));
        Assert.Equal(JsonValueKind.Null, (await SettingsAsync(session)).GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task Plex_settings_and_linking_need_the_session_never_an_api_key()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (keyClient, _) = await CompatTestHelpers.KeyClientAsync(factory, session, "script", "default", header: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.GetAsync("/api/v1/auth/external")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.PutAsJsonAsync("/api/v1/auth/external/plex", new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.PostAsJsonAsync("/api/v1/auth/external/plex/link", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.DeleteAsync("/api/v1/auth/external/plex/link")).StatusCode);

        // A link started by the session is finished by the session only.
        var link = await StartAsync(session, "/api/v1/auth/external/plex/link");
        plexTv.Approve(plexTv.LastPinId, FakePlexTvSignIn.Owner);
        var anonymous = factory.CreateAnonymousClient();
        anonymous.DefaultRequestHeaders.Add("Cookie", $"{ExternalLoginEndpoints.FlowCookie}={link.GetProperty("flowId").GetString()}");
        var stolen = await anonymous.PostAsJsonAsync("/api/v1/auth/external/poll", new { flowId = link.GetProperty("flowId").GetString() });
        Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);
        Assert.Equal("linked", await PollAsync(session, link));

        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync("/api/v1/auth/external/jellyfin", new { enabled = true })).StatusCode);
    }

    [Fact]
    public async Task The_plex_client_identifier_is_generated_once_per_install_and_sent_to_plex_tv_and_plex_servers()
    {
        await using var plexTv = await FakePlexTvSignIn.StartAsync();
        await using var plex = await Playback.FakePlex.StartAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"bigarr-api-test-{Guid.NewGuid():N}");
        var settings = new Dictionary<string, string> { ["Bigarr:PlexTv:BaseUrl"] = plexTv.Url };
        string first;
        try
        {
            await using (var factory = new ConfiguredBigarrApiFactory(directory, settings, deleteOnDispose: false))
            {
                var session = await CompatTestHelpers.SessionAsync(factory);
                using (var scope = factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                    Assert.Empty(db.InstanceSettings); // nothing until Bigarr first talks to Plex
                }

                await LinkAsync(session, plexTv, FakePlexTvSignIn.Owner);
                first = StoredClientIdentifier(factory);
                Assert.True(Guid.TryParse(first, out _));
                Assert.Equal([first], plexTv.ClientIdentifiers);

                // The playback side (a Plex server) gets the same identifier.
                int serverId;
                using (var scope = factory.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                    var server = db.MediaServers.Add(new Bigarr.Domain.MediaServers.MediaServer { Name = "Plex", Url = plex.Url, Token = Playback.FakePlex.Token }).Entity;
                    await db.SaveChangesAsync();
                    serverId = server.Id;
                }

                await session.PostAsync($"/api/v1/mediaservers/{serverId}/test", content: null);
                Assert.NotEmpty(plex.Requests);
                Assert.Contains(plex.Requests, r => r.Headers.TryGetValue("X-Plex-Client-Identifier", out var id) && id == first);
                Assert.DoesNotContain(plex.Requests, r => r.Headers.TryGetValue("X-Plex-Client-Identifier", out var id) && id != first);
            }

            // A restart keeps it.
            await using (var restarted = new ConfiguredBigarrApiFactory(directory, settings))
            {
                var session = await CompatTestHelpers.SessionAsync(restarted);
                await StartAsync(session, "/api/v1/auth/external/plex/link");
                Assert.Equal(first, StoredClientIdentifier(restarted));
                Assert.Equal([first], plexTv.ClientIdentifiers);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        // Another install gets its own.
        await using var other = new Factory(plexTv.Url);
        await StartAsync(await CompatTestHelpers.SessionAsync(other), "/api/v1/auth/external/plex/link");
        Assert.NotEqual(first, StoredClientIdentifier(other));
    }

    private static string StoredClientIdentifier(BigarrApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BigarrDbContext>().InstanceSettings.Single().PlexClientIdentifier!;
    }

    private static async Task LinkAsync(HttpClient session, FakePlexTvSignIn plexTv, FakePlexTvSignIn.Account account)
    {
        var link = await StartAsync(session, "/api/v1/auth/external/plex/link");
        plexTv.Approve(plexTv.LastPinId, account);
        Assert.Equal("linked", await PollAsync(session, link));
    }

    private static async Task EnableAsync(HttpClient session, bool enabled)
    {
        var response = await session.PutAsJsonAsync("/api/v1/auth/external/plex", new { enabled });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> StartAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> PollAsync(HttpClient client, JsonElement flow) =>
        (await PollResponseAsync(client, flow)).GetProperty("status").GetString()!;

    private static async Task<JsonElement> PollResponseAsync(HttpClient client, JsonElement flow)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/external/poll", new { flowId = flow.GetProperty("flowId").GetString() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement[]> ProvidersAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement[]>("/api/v1/auth/providers") ?? [];

    private static async Task<JsonElement> SettingsAsync(HttpClient session)
    {
        var all = await session.GetFromJsonAsync<JsonElement[]>("/api/v1/auth/external");
        return Assert.Single(all!, p => p.GetProperty("key").GetString() == "plex");
    }

    private sealed class Factory(string plexTvUrl, TimeProvider? time = null)
        : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:PlexTv:BaseUrl"] = plexTvUrl }, deleteOnDispose: true, time)
    {
        private readonly LogCapture _logs = new();

        /// <summary>Everything the app logged, to check what it never writes.</summary>
        public string Logs => string.Join('\n', _logs.Lines);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Lines.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
