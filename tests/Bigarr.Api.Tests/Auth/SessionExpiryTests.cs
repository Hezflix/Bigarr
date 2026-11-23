using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Tests.Metadata;

namespace Bigarr.Api.Tests.Auth;

public sealed class SessionExpiryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_session_expires_on_the_apps_clock_not_on_the_systems()
    {
        var clock = new MutableTimeProvider(Start);
        using var factory = new ConfiguredBigarrApiFactory(null, null, time: clock);
        var client = await LoginAsync(factory);

        // Years before the real date, the session is open: the system clock plays no part.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

        clock.Advance(TimeSpan.FromDays(15));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private static async Task<HttpClient> LoginAsync(BigarrApiFactory factory)
    {
        var client = factory.CreateClientWithCookies();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }
}
