using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// The whole loop in the running app: a saved Plex server is followed at once over its event stream, a film that plays
/// shows in Now playing, its stop becomes a play on the right version, and the server falls back to polling when the
/// event stream is refused.
/// </summary>
public sealed class PlaybackApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task A_film_played_on_plex_shows_in_now_playing_then_in_the_plays_of_its_version()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        int movieId;
        int versionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var (movie, version) = await TestData.SeedMovieAsync(db, tmdbId: 990001);
            db.MediaFiles.Add(TestData.NewFile(version, "Invented.Film.2020.1080p.mkv"));
            await db.SaveChangesAsync();
            movieId = movie.Id;
            versionId = version.Id;
        }

        await using var plex = await FakePlex.StartAsync();
        plex.Items["500"] = new FakePlex.Item("500", "Invented Film", 2020, 6_000_000, "3", [new FakePlex.Part("5001", "/media/movies/The Matrix (1999)/Invented.Film.2020.1080p.mkv", "1080")], TmdbId: 990001);

        var settings = await session.PutAsJsonAsync("/api/v1/playback/settings", new { minimumPlay = 0, minimumPlayUnit = "seconds", watchedPercent = 90 });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync("/api/v1/playback/settings", new { minimumPlay = 150, minimumPlayUnit = "percent", watchedPercent = 90 })).StatusCode);

        var created = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Plex playback", kind = "plex", url = plex.Url, token = FakePlex.Token, enabled = true, pathMappings = new[] { new { from = "/data", to = "/media" } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var serverId = (await created.ReadJsonAsync()).GetProperty("id").GetInt32();
        await plex.WaitForListenerAsync();

        // A new stream: its first notification makes Bigarr read the list of streams.
        plex.Set(new FakePlex.Stream("41", "s-41", "500", "7", "dana", "5001", Address: "198.51.100.7"));
        await plex.SendAsync("41", "500", "playing", 0);
        var playing = await WaitUntilAsync(async () =>
        {
            var now = await (await session.GetAsync("/api/v1/playback/sessions")).ReadJsonAsync();
            return now.GetProperty("sessions").GetArrayLength() == 1 ? now : (JsonElement?)null;
        });
        var live = playing.GetProperty("sessions")[0];
        Assert.Equal("dana", live.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("Invented Film", live.GetProperty("item").GetProperty("title").GetString());
        Assert.Equal("1080p", live.GetProperty("versionName").GetString());
        Assert.Equal("directPlay", live.GetProperty("details").GetProperty("decision").GetString());
        var status = playing.GetProperty("servers").EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == serverId);
        Assert.True(status.GetProperty("connected").GetBoolean());

        plex.Remove("41");
        await plex.SendAsync("41", "500", "stopped", 5_995_000);
        var plays = await WaitUntilAsync(async () =>
        {
            var page = await (await session.GetAsync($"/api/v1/playback/plays?movieId={movieId}")).ReadJsonAsync();
            return page.GetProperty("totalRecords").GetInt32() == 1 ? page : (JsonElement?)null;
        });
        var play = plays.GetProperty("records")[0];
        Assert.True(play.GetProperty("watched").GetBoolean());
        Assert.Equal(versionId, play.GetProperty("versionId").GetInt32());
        Assert.Equal("Living room TV", play.GetProperty("player").GetString());

        var streams = await (await session.GetAsync($"/api/v1/playback/plays/{play.GetProperty("id").GetInt32()}/streams")).ReadJsonAsync();
        var stream = streams.EnumerateArray().Single();
        Assert.Equal("198.51.100.7", stream.GetProperty("details").GetProperty("ipAddress").GetString());
        Assert.Equal("eac3", stream.GetProperty("details").GetProperty("sourceAudioCodec").GetString());
        Assert.Equal("/data/movies/The Matrix (1999)/Invented.Film.2020.1080p.mkv", stream.GetProperty("filePath").GetString());
        Assert.Equal("1080p", stream.GetProperty("versionName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync("/api/v1/playback/plays/999999/streams")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/playback/plays?sort=played&direction=asc")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.GetAsync("/api/v1/playback/plays?sort=nonsense")).StatusCode);

        var perVersion = await (await session.GetAsync($"/api/v1/playback/movies/{movieId}")).ReadJsonAsync();
        var row = perVersion.EnumerateArray().Single();
        Assert.Equal(versionId, row.GetProperty("versionId").GetInt32());
        Assert.Equal(1, row.GetProperty("plays").GetInt32());
        Assert.Equal(1, row.GetProperty("users").GetInt32());

        var users = await (await session.GetAsync("/api/v1/playback/users")).ReadJsonAsync();
        Assert.Contains(users.EnumerateArray(), u => u.GetProperty("name").GetString() == "dana" && u.GetProperty("plays").GetInt32() == 1);

        // The history hangs off the server: deleting it is refused, disabling is the way.
        Assert.Equal(HttpStatusCode.Conflict, (await session.DeleteAsync($"/api/v1/mediaservers/{serverId}")).StatusCode);
        var disabled = await session.PutAsJsonAsync($"/api/v1/mediaservers/{serverId}", new { name = "Plex playback", kind = "plex", url = plex.Url, token = FakePlex.Token, enabled = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
    }

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
