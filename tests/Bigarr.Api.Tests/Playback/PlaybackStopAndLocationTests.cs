using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// Stopping a stream from Now playing reaches Plex with the stream's session id and the message, and only on request;
/// the place of a public address is looked up when shown (Now playing, a play's streams, the user's addresses) and never
/// stored.
/// </summary>
public sealed class PlaybackStopAndLocationTests
{
    [Fact]
    public async Task A_stream_is_stopped_on_request_and_its_place_follows_it_into_the_history()
    {
        await using var plexTv = await FakePlexTv.StartAsync();
        await using var plex = await FakePlex.StartAsync();
        await using var factory = new Factory(plexTv.Url);
        var session = await CompatTestHelpers.SessionAsync(factory);
        plex.Items["500"] = new FakePlex.Item("500", "Invented Film", 2020, 6_000_000, "3", [new FakePlex.Part("5001", "/media/movies/Invented Film (2020)/Invented.Film.2020.1080p.mkv", "1080")]);
        Assert.Equal(HttpStatusCode.OK, (await session.PutAsJsonAsync("/api/v1/playback/settings", new { minimumPlay = 0, minimumPlayUnit = "seconds", watchedPercent = 90 })).StatusCode);
        var created = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Plex", kind = "plex", url = plex.Url, token = FakePlex.Token, enabled = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await plex.WaitForListenerAsync();

        plex.Set(new FakePlex.Stream("41", "s-41", "500", "7", "dana", "5001", Address: "198.51.100.7"));
        plex.Set(new FakePlex.Stream("42", "s-42", "500", "8", "eli", "5001", Address: "192.168.1.30", Local: true));
        await plex.SendAsync("41", "500", "playing", 0);
        var now = await WaitUntilAsync(async () =>
        {
            var body = await (await session.GetAsync("/api/v1/playback/sessions")).ReadJsonAsync();
            return body.GetProperty("sessions").GetArrayLength() == 2 ? body : (JsonElement?)null;
        });
        var dana = now.GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("user").GetProperty("name").GetString() == "dana");
        var eli = now.GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("user").GetProperty("name").GetString() == "eli");
        Assert.True(dana.GetProperty("canStop").GetBoolean());
        Assert.False(dana.GetProperty("location").GetProperty("local").GetBoolean());
        Assert.Equal("Lyon", dana.GetProperty("location").GetProperty("city").GetString());
        Assert.Equal("FR", dana.GetProperty("location").GetProperty("countryCode").GetString());
        Assert.True(eli.GetProperty("location").GetProperty("local").GetBoolean());
        Assert.Equal(JsonValueKind.Null, eli.GetProperty("location").GetProperty("city").ValueKind);

        // Showing Now playing again does not ask plex.tv again; a local address is never sent.
        await session.GetAsync("/api/v1/playback/sessions");
        Assert.Equal(1, plexTv.Lookups("198.51.100.7"));
        Assert.DoesNotContain(plexTv.Requests, r => r.Query.Contains("192.168.1.30", StringComparison.Ordinal));

        // Nothing reached Plex's terminate on its own.
        Assert.DoesNotContain(plex.Requests, r => r.Path == "/status/sessions/terminate");

        var danaId = dana.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostAsJsonAsync($"/api/v1/playback/sessions/{danaId}/stop", new { message = new string('x', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PostAsJsonAsync("/api/v1/playback/sessions/999999/stop", new { message = (string?)null })).StatusCode);

        plex.NoPlexPass = true;
        var refused = await session.PostAsJsonAsync($"/api/v1/playback/sessions/{danaId}/stop", new { message = (string?)null });
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.Contains("Plex Pass", (await refused.ReadJsonAsync()).GetProperty("detail").GetString(), StringComparison.Ordinal);

        plex.NoPlexPass = false;
        var stopped = await session.PostAsJsonAsync($"/api/v1/playback/sessions/{danaId}/stop", new { message = "Server maintenance, back in 10 minutes" });
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        var terminate = plex.Requests.Last(r => r.Path == "/status/sessions/terminate");
        var query = System.Web.HttpUtility.ParseQueryString(terminate.Query);
        Assert.Equal("s-41", query["sessionId"]);
        Assert.Equal("Server maintenance, back in 10 minutes", query["reason"]);

        // Plex then says the stream stopped, like for any other end.
        await plex.SendAsync("41", "500", "stopped", 600_000);
        var plays = await WaitUntilAsync(async () =>
        {
            var page = await (await session.GetAsync("/api/v1/playback/plays")).ReadJsonAsync();
            return page.GetProperty("totalRecords").GetInt32() == 1 ? page : (JsonElement?)null;
        });
        var play = plays.GetProperty("records")[0];
        var streams = await (await session.GetAsync($"/api/v1/playback/plays/{play.GetProperty("id").GetInt32()}/streams")).ReadJsonAsync();
        Assert.Equal("Lyon", streams[0].GetProperty("location").GetProperty("city").GetString());
        Assert.Equal("Auvergne-Rhône-Alpes", streams[0].GetProperty("location").GetProperty("region").GetString());

        var userId = play.GetProperty("user").GetProperty("id").GetInt32();
        var addresses = await (await session.GetAsync($"/api/v1/playback/users/{userId}/addresses")).ReadJsonAsync();
        var address = addresses.EnumerateArray().Single();
        Assert.Equal("198.51.100.7", address.GetProperty("ipAddress").GetString());
        Assert.Equal("France", address.GetProperty("location").GetProperty("country").GetString());
        Assert.Equal(1, address.GetProperty("streams").GetInt32());
        Assert.Equal("Living room TV", address.GetProperty("lastPlayer").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync("/api/v1/playback/users/999999/addresses")).StatusCode);

        // The place is not stored: a recorded stream from another address is placed when shown.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var row = await db.PlayStreams.SingleAsync();
            row.Details.IpAddress = "203.0.113.20";
            await db.SaveChangesAsync();
        }

        var older = await (await session.GetAsync($"/api/v1/playback/plays/{play.GetProperty("id").GetInt32()}/streams")).ReadJsonAsync();
        Assert.Equal("Porto", older[0].GetProperty("location").GetProperty("city").GetString());
        Assert.Equal("PT", older[0].GetProperty("location").GetProperty("countryCode").GetString());
    }

    private sealed class Factory(string plexTvUrl)
        : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:PlexTv:BaseUrl"] = plexTvUrl }, deleteOnDispose: true);

    private static async Task<JsonElement> WaitUntilAsync(Func<Task<JsonElement?>> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was never met.");
            }

            await Task.Delay(100);
        }
    }
}
