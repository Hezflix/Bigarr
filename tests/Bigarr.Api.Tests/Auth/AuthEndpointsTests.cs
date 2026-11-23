using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Tests.Indexers;

namespace Bigarr.Api.Tests.Auth;

public sealed class AuthEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_api_requires_authentication()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/v1/system/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_valid_credentials_opens_a_session()
    {
        var client = await LoginAsync();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        Assert.NotNull(me);
        Assert.Equal(BigarrApiFactory.AdminUsername, me.Username);
        Assert.Equal("Admin", me.Role);
        Assert.Equal("Session", me.AuthKind);
        Assert.Null(me.CompatView);

        var status = await client.GetAsync("/api/v1/system/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_rejected()
    {
        var response = await factory.CreateClientWithCookies().PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, "nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_are_refused_for_a_while_then_the_right_one_works()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
        using var app = new ConfiguredBigarrApiFactory(
            null,
            new Dictionary<string, string> { ["Bigarr:SignIn:MaxFailuresPerUsername"] = "3" },
            time: clock);
        var client = app.CreateClientWithCookies();

        for (var i = 0; i < 3; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, "nope"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // Past the limit even the right password waits, and the answer says for how long.
        var refused = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(15), refused.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        // A name that does not exist goes through the same 401 then 429 sequence.
        for (var i = 0; i < 3; i++)
        {
            var ghost = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ghost", "nope"));
            Assert.Equal(HttpStatusCode.Unauthorized, ghost.StatusCode);
        }

        var ghostRefused = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ghost", "nope"));
        Assert.Equal(HttpStatusCode.TooManyRequests, ghostRefused.StatusCode);
        Assert.NotNull(ghostRefused.Headers.RetryAfter);

        clock.Advance(TimeSpan.FromMinutes(16));
        var signedIn = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
    }

    [Fact]
    public async Task Logout_closes_the_session()
    {
        var client = await LoginAsync();

        var logout = await client.PostAsync("/api/v1/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_api_key_authenticates_by_header_and_by_query_string()
    {
        var session = await LoginAsync();
        var created = await CreateKeyAsync(session, "seerr-4k", "version:4k");
        Assert.StartsWith("bgr_", created.Key);
        Assert.Equal("version:4k", created.CompatView);

        var byHeader = factory.CreateAnonymousClient();
        byHeader.DefaultRequestHeaders.Add("X-Api-Key", created.Key);
        var me = await byHeader.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        Assert.NotNull(me);
        Assert.Equal("ApiKey", me.AuthKind);
        Assert.Equal("version:4k", me.CompatView);

        var byQuery = await factory.CreateAnonymousClient()
            .GetFromJsonAsync<MeResponse>($"/api/v1/auth/me?apikey={created.Key}");
        Assert.NotNull(byQuery);
        Assert.Equal(BigarrApiFactory.AdminUsername, byQuery.Username);
    }

    [Fact]
    public async Task An_api_key_cannot_manage_api_keys()
    {
        var session = await LoginAsync();
        var created = await CreateKeyAsync(session, "script", null);

        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", created.Key);

        var list = await client.GetAsync("/api/v1/apikeys");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task A_revoked_api_key_is_rejected()
    {
        var session = await LoginAsync();
        var created = await CreateKeyAsync(session, "bazarr", "all");

        var revoke = await session.DeleteAsync($"/api/v1/apikeys/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", created.Key);
        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var keys = await session.GetFromJsonAsync<List<ApiKeyResponse>>("/api/v1/apikeys");
        Assert.NotNull(keys);
        Assert.NotNull(keys.Single(k => k.Id == created.Id).RevokedAt);
    }

    [Fact]
    public async Task An_invalid_compat_view_is_rejected()
    {
        var session = await LoginAsync();

        var response = await session.PostAsJsonAsync("/api/v1/apikeys", new CreateApiKeyRequest("broken", "version:Not A Slug"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_routes_outside_the_api_do_not_leak_into_the_api()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_every_other_session_and_keeps_the_one_that_changed_it()
    {
        using var app = new ConfiguredBigarrApiFactory(null, null);
        async Task<HttpClient> OpenAsync(string password)
        {
            var opened = app.CreateClientWithCookies();
            Assert.Equal(HttpStatusCode.NoContent, (await opened.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, password))).StatusCode);
            return opened;
        }

        var mine = await OpenAsync(BigarrApiFactory.AdminPassword);
        var other = await OpenAsync(BigarrApiFactory.AdminPassword);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/auth/me")).StatusCode);

        const string changed = "another-password-456";
        Assert.Equal(HttpStatusCode.NoContent, (await mine.PostAsJsonAsync("/api/v1/auth/password", new ChangePasswordRequest(BigarrApiFactory.AdminPassword, changed))).StatusCode);

        // The session opened with the old password is over; the one that changed it goes on; the new password opens new ones.
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await OpenAsync(changed)).GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreateClientWithCookies();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static async Task<ApiKeyCreatedResponse> CreateKeyAsync(HttpClient session, string name, string? compatView)
    {
        var response = await session.PostAsJsonAsync("/api/v1/apikeys", new CreateApiKeyRequest(name, compatView));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();
        Assert.NotNull(created);
        return created;
    }
}
