using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Auth;

namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// A key has a scope: <c>compat</c> opens the Radarr- and Sonarr-compatible surfaces and the followed apps' webhooks,
/// <c>full</c> also opens Bigarr's own API, <c>calendar</c> opens the iCal feed and nothing else. A key's owner's role
/// and the key's compat view are separate checks.
/// </summary>
public sealed class ApiKeyScopeTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly string[] AdminApiRoutes =
    [
        "/api/v1/movies",
        "/api/v1/system/status",
        "/api/v1/auth/me",
        "/api/v1/requests",
        "/api/v1/calendar/bigarr.ics",
        "/api/v1/calendar",
        "/api/v1/filesystem",
    ];

    [Fact]
    public async Task A_new_key_is_compat_by_default_and_opens_only_the_compat_surfaces()
    {
        var session = await SessionAsync();
        var key = await CreateAsync(session, new { name = "seerr", compatView = "default" });
        Assert.Equal("compat", key.Scope);
        var client = KeyClient(key.Key);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sonarr/api/v3/system/status")).StatusCode);
        foreach (var route in AdminApiRoutes)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        }

        // The refusal says which scope the key has, never the key.
        var refused = await client.GetAsync("/api/v1/movies");
        var body = await refused.Content.ReadAsStringAsync();
        Assert.Contains("compat", body, StringComparison.Ordinal);
        Assert.DoesNotContain(key.Key, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_key_opens_what_a_key_opened_before()
    {
        var session = await SessionAsync();
        var key = await CreateAsync(session, new { name = "script", scope = "full" });
        Assert.Equal("full", key.Scope);
        var client = KeyClient(key.Key);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/movies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/calendar/bigarr.ics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/radarr/api/v3/system/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sonarr/api/v3/system/status")).StatusCode);

        // Managing keys stays the session's.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/apikeys")).StatusCode);
    }

    [Fact]
    public async Task A_calendar_key_opens_the_feed_and_nothing_else()
    {
        var session = await SessionAsync();
        var key = await CreateAsync(session, new { name = "phone calendar", scope = "calendar" });
        Assert.Equal("calendar", key.Scope);

        var feed = await factory.CreateAnonymousClient().GetAsync($"/api/v1/calendar/bigarr.ics?apikey={key.Key}");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        Assert.Equal("text/calendar", feed.Content.Headers.ContentType?.MediaType);

        var client = KeyClient(key.Key);
        foreach (var route in new[] { "/api/v1/calendar", "/api/v1/movies", "/api/v1/auth/me", "/radarr/api/v3/system/status", "/sonarr/api/v3/system/status" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        }
    }

    [Fact]
    public async Task A_calendar_key_takes_no_view()
    {
        var session = await SessionAsync();

        var response = await session.PostAsJsonAsync("/api/v1/apikeys", new { name = "calendar", compatView = "all", scope = "calendar" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("7")]
    public async Task An_unknown_scope_is_rejected(string scope)
    {
        var session = await SessionAsync();

        var response = await session.PostAsJsonAsync("/api/v1/apikeys", new { name = "odd", scope });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_keys_scope_can_be_changed_from_the_session_only()
    {
        var session = await SessionAsync();
        var key = await CreateAsync(session, new { name = "script" });
        var client = KeyClient(key.Key);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/movies")).StatusCode);

        var promoted = await session.PutAsJsonAsync($"/api/v1/apikeys/{key.Id}", new { scope = "full" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal("full", (await promoted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("scope").GetString());
        var listed = await session.GetFromJsonAsync<JsonElement>("/api/v1/apikeys");
        Assert.Equal("full", listed.EnumerateArray().Single(k => k.GetProperty("id").GetInt32() == key.Id).GetProperty("scope").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/movies")).StatusCode);

        // A key can never change a scope, not even its own.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/v1/apikeys/{key.Id}", new { scope = "calendar" })).StatusCode);

        // A key that has a view other than the default cannot become a calendar key.
        var viewed = await CreateAsync(session, new { name = "bazarr", compatView = "all" });
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync($"/api/v1/apikeys/{viewed.Id}", new { scope = "calendar" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync($"/api/v1/apikeys/{viewed.Id}", new { scope = "root" })).StatusCode);

        // A revoked key, like a missing one, is not found.
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/apikeys/{key.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync($"/api/v1/apikeys/{key.Id}", new { scope = "compat" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync("/api/v1/apikeys/99999", new { scope = "compat" })).StatusCode);
    }

    [Fact]
    public async Task A_compat_key_still_wakes_a_followed_app_and_a_calendar_key_does_not()
    {
        var session = await SessionAsync();
        var compat = KeyClient((await CreateAsync(session, new { name = "radarr webhook" })).Key);
        var calendar = KeyClient((await CreateAsync(session, new { name = "calendar", scope = "calendar" })).Key);

        // The followed app does not exist here: what matters is that the compat key gets as far as the lookup.
        Assert.Equal(HttpStatusCode.NotFound, (await compat.PostAsync("/api/v1/shadow/radarrs/9999/webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await compat.PostAsync("/api/v1/shadow/sonarrs/9999/webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await calendar.PostAsync("/api/v1/shadow/radarrs/1/webhook", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await calendar.PostAsync("/api/v1/shadow/sonarrs/1/webhook", null)).StatusCode);

        // The rest of the shadow API stays admin API: a compat key does not open it.
        Assert.Equal(HttpStatusCode.Forbidden, (await compat.GetAsync("/api/v1/shadow/radarrs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await compat.PostAsync("/api/v1/shadow/radarrs/9999/sync", null)).StatusCode);
    }

    [Fact]
    public async Task A_compat_key_reads_the_covers_under_the_url_base_and_a_calendar_key_does_not()
    {
        var session = await SessionAsync();
        var compat = KeyClient((await CreateAsync(session, new { name = "bazarr" })).Key);
        var calendar = KeyClient((await CreateAsync(session, new { name = "calendar", scope = "calendar" })).Key);

        foreach (var route in new[] { "/radarr/MediaCover/1/poster.jpg", "/sonarr/MediaCover/1/poster.jpg" })
        {
            // No such film or series here: 404 is fine, a refusal is not.
            var opened = (await compat.GetAsync(route)).StatusCode;
            Assert.NotEqual(HttpStatusCode.Unauthorized, opened);
            Assert.NotEqual(HttpStatusCode.Forbidden, opened);
            Assert.Equal(HttpStatusCode.Forbidden, (await calendar.GetAsync(route)).StatusCode);
        }
    }

    private async Task<HttpClient> SessionAsync()
    {
        var client = factory.CreateClientWithCookies();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private HttpClient KeyClient(string key)
    {
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<(int Id, string Key, string Scope)> CreateAsync(HttpClient session, object request)
    {
        var response = await session.PostAsJsonAsync("/api/v1/apikeys", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetInt32(), body.GetProperty("key").GetString()!, body.TryGetProperty("scope", out var scope) ? scope.GetString()! : string.Empty);
    }
}
