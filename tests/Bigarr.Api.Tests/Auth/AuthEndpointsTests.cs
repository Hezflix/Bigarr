using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Auth;

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
