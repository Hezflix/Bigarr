using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Movies;

/// <summary>Changing a version's settings is a business fact: it reaches the outbox once, and only when something changed.</summary>
public sealed class VersionUpdatedEventTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private const int DunePartTwo = 693134;

    [Fact]
    public async Task Toggling_monitoring_publishes_versionUpdated_and_a_no_op_put_publishes_nothing()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var (hd, _, root) = await CompatTestHelpers.SetupAsync(session);

        var added = await session.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(DunePartTwo, hd, root.Id, Monitored: true));
        JsonElement movie;
        if (added.StatusCode == HttpStatusCode.Conflict)
        {
            var list = await (await session.GetAsync("/api/v1/movies")).ReadJsonAsync();
            movie = list.EnumerateArray().Single(m => m.GetProperty("tmdbId").GetInt32() == DunePartTwo);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            movie = await added.ReadJsonAsync();
        }

        var movieId = movie.GetProperty("id").GetInt32();
        var versionId = movie.GetProperty("versions").EnumerateArray().First().GetProperty("id").GetInt32();
        var before = await CountAsync(versionId);

        var toggled = await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{versionId}", new UpdateVersionRequest(Monitored: false));
        Assert.Equal(HttpStatusCode.OK, toggled.StatusCode);
        Assert.Equal(before + 1, await CountAsync(versionId));

        // Seerr re-sends the whole resource on every sync: an unchanged version must not flood the consumers.
        var unchanged = await session.PutAsJsonAsync($"/api/v1/movies/{movieId}/versions/{versionId}", new UpdateVersionRequest(Monitored: false));
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(before + 1, await CountAsync(versionId));
    }

    [Fact]
    public async Task The_session_cookie_is_secure_when_the_proxy_says_https()
    {
        var plain = await factory.CreateAnonymousClient().PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, plain.StatusCode);
        Assert.DoesNotContain("secure", string.Join(";", plain.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);

        var proxied = factory.CreateAnonymousClient();
        proxied.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var secure = await proxied.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, secure.StatusCode);
        Assert.Contains("secure", string.Join(";", secure.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> CountAsync(int versionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var payloads = await db.Outbox.Where(e => e.Type == "versionUpdated").Select(e => e.Payload).ToListAsync();
        return payloads.Count(p => JsonDocument.Parse(p).RootElement.GetProperty("version").GetProperty("id").GetInt32() == versionId);
    }
}
