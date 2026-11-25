using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Infrastructure.Auth;

namespace Bigarr.Api.Tests.Auth;

/// <summary>
/// A request that changes something with the session cookie must come from Bigarr's own pages: the browser's
/// <c>Sec-Fetch-Site</c>, or failing that the host of its <c>Origin</c>, says so. Requests with no cookie are not concerned.
/// </summary>
public sealed class SameOriginGuardTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private const string Unsafe = "/api/v1/system/tasks/doesNotExist/run";

    [Theory]
    [InlineData("GET", true, "cross-site", null, "bigarr.example.com", false)]
    [InlineData("HEAD", true, "cross-site", null, "bigarr.example.com", false)]
    [InlineData("POST", false, "cross-site", "https://evil.example", "bigarr.example.com", false)]
    [InlineData("POST", true, "same-origin", null, "bigarr.example.com", false)]
    [InlineData("POST", true, "none", null, "bigarr.example.com", false)]
    [InlineData("POST", true, "same-site", null, "bigarr.example.com", true)]
    [InlineData("POST", true, "cross-site", null, "bigarr.example.com", true)]
    [InlineData("DELETE", true, "cross-site", null, "bigarr.example.com", true)]
    [InlineData("PUT", true, "same-site", null, "bigarr.example.com", true)]
    [InlineData("POST", true, null, "http://192.168.1.10:7979", "192.168.1.10:7979", false)]
    [InlineData("POST", true, null, "http://192.168.1.10:8080", "192.168.1.10:7979", true)]
    [InlineData("POST", true, null, "https://bigarr.example.com", "bigarr.example.com", false)]
    [InlineData("POST", true, null, "https://BIGARR.example.com", "bigarr.example.com", false)]
    [InlineData("POST", true, null, "https://bigarr.example.com:443", "bigarr.example.com", false)]
    [InlineData("POST", true, null, "null", "bigarr.example.com", true)]
    [InlineData("POST", true, null, "not a url", "bigarr.example.com", true)]
    [InlineData("POST", true, null, null, "bigarr.example.com", false)]
    [InlineData("POST", true, "same-origin", "https://evil.example", "bigarr.example.com", false)]
    public void The_rule_refuses_only_what_a_browser_says_comes_from_elsewhere(
        string method,
        bool cookie,
        string? site,
        string? origin,
        string host,
        bool refused)
    {
        var reason = SameOriginGuard.Refusal(method, cookie, site, origin, host);
        Assert.Equal(refused, reason is not null);
    }

    [Theory]
    [InlineData("cross-site", HttpStatusCode.Forbidden)]
    [InlineData("same-site", HttpStatusCode.Forbidden)]
    [InlineData("same-origin", HttpStatusCode.NotFound)]
    [InlineData("none", HttpStatusCode.NotFound)]
    public async Task A_session_request_is_judged_on_sec_fetch_site(string site, HttpStatusCode expected)
    {
        var client = await SessionAsync(factory);
        var response = await SendAsync(client, HttpMethod.Post, Unsafe, ("Sec-Fetch-Site", site));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task A_refusal_says_what_to_forward()
    {
        var client = await SessionAsync(factory);
        var response = await SendAsync(client, HttpMethod.Post, Unsafe, ("Sec-Fetch-Site", "cross-site"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Host", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_sec_fetch_site_the_origin_is_judged_against_the_host()
    {
        var client = await SessionAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, Unsafe, ("Origin", "http://evil.example"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, Unsafe, ("Origin", "http://localhost:8080"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, Unsafe, ("Origin", "null"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, Unsafe, ("Origin", "http://localhost"))).StatusCode);
    }

    [Fact]
    public async Task The_host_a_proxy_forwards_is_the_host_the_origin_is_compared_with()
    {
        var client = await SessionAsync(factory);
        var forwarded = await SendAsync(
            client,
            HttpMethod.Post,
            Unsafe,
            ("X-Forwarded-Host", "bigarr.example.com"),
            ("Origin", "https://bigarr.example.com"));
        Assert.Equal(HttpStatusCode.NotFound, forwarded.StatusCode);

        var foreign = await SendAsync(
            client,
            HttpMethod.Post,
            Unsafe,
            ("X-Forwarded-Host", "bigarr.example.com"),
            ("Origin", "https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task A_client_with_the_cookie_and_neither_header_goes_through()
    {
        var client = await SessionAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, Unsafe)).StatusCode);
    }

    [Fact]
    public async Task A_safe_request_is_never_refused()
    {
        var client = await SessionAsync(factory);
        var response = await SendAsync(client, HttpMethod.Get, "/api/v1/system/tasks", ("Sec-Fetch-Site", "cross-site"), ("Origin", "http://evil.example"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_api_key_without_the_cookie_is_not_concerned()
    {
        var session = await SessionAsync(factory);
        var created = await session.PostAsJsonAsync("/api/v1/apikeys", new { name = "script", scope = "full" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var key = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;

        var keyed = factory.CreateAnonymousClient();
        var response = await SendAsync(
            keyed,
            HttpMethod.Post,
            Unsafe,
            ("X-Api-Key", key),
            ("Sec-Fetch-Site", "cross-site"),
            ("Origin", "http://evil.example"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The compatible surfaces answer to the key the same way.
        var compat = await SendAsync(
            keyed,
            HttpMethod.Post,
            "/radarr/api/v3/command",
            ("X-Api-Key", key),
            ("Sec-Fetch-Site", "cross-site"),
            ("Origin", "http://evil.example"));
        Assert.NotEqual(HttpStatusCode.Forbidden, compat.StatusCode);
    }

    [Fact]
    public async Task Signing_in_with_a_stale_cookie_from_the_same_origin_works()
    {
        var client = await SessionAsync(factory);
        var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            JsonContent.Create(new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword)),
            ("Sec-Fetch-Site", "same-origin"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task The_check_can_be_turned_off()
    {
        using var open = new ConfiguredBigarrApiFactory(
            null,
            new Dictionary<string, string> { ["Bigarr:Auth:RequireSameOrigin"] = "false" });
        var client = await SessionAsync(open);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Post, Unsafe, ("Sec-Fetch-Site", "cross-site"))).StatusCode);
    }

    private static async Task<HttpClient> SessionAsync(BigarrApiFactory factory)
    {
        var client = factory.CreateClientWithCookies();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, params (string Name, string Value)[] headers) =>
        SendAsync(client, method, url, null, headers);

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, HttpContent? content, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        return client.SendAsync(request);
    }
}
