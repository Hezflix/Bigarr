using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// Now playing's pictures come from Bigarr only: an episode of a series Bigarr follows gets the series' poster and
/// fanart (matched by the show's TVDB id), an episode of a show it does not know and a film it does not manage get none.
/// </summary>
public sealed class PlaybackArtworkTests
{
    [Fact]
    public async Task An_episode_of_a_series_in_bigarr_gets_its_images_and_an_unknown_show_gets_none()
    {
        await using var plex = await FakePlex.StartAsync();
        await using var factory = new BigarrApiFactory();
        var session = await CompatTestHelpers.SessionAsync(factory);
        plex.Items["600"] = new FakePlex.Item("600", "Pilot", 2019, 3_000_000, "4", [new FakePlex.Part("6001", "/media/tv/Followed Show/Season 02/Followed.Show.S02E01.mkv", "1080")], Type: "episode", ShowRatingKey: "60", ShowTitle: "Followed Show", Season: 2, Episode: 1);
        plex.Items["700"] = new FakePlex.Item("700", "Pilot", 2021, 3_000_000, "4", [new FakePlex.Part("7001", "/media/tv/Unknown Show/Season 01/Unknown.Show.S01E01.mkv", "1080")], Type: "episode", ShowRatingKey: "70", ShowTitle: "Unknown Show", Season: 1, Episode: 1);
        plex.Items["500"] = new FakePlex.Item("500", "Invented Film", 2020, 6_000_000, "3", [new FakePlex.Part("5001", "/media/movies/Invented Film (2020)/Invented.Film.mkv", "1080")]);
        plex.ShowTvdbIds["60"] = 777001;
        plex.ShowTvdbIds["70"] = 777002;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var root = db.RootFolders.Add(new Bigarr.Domain.Library.RootFolder { Path = "/data/tv" }).Entity;
            await db.SaveChangesAsync();
            var followed = db.Series.Add(new Bigarr.Domain.Tv.Series
            {
                Title = "Followed Show",
                SortTitle = "followed show",
                TvdbId = 777001,
                DefaultRootFolderId = root.Id,
                Images = [new MediaImage(ImageType.Poster, "https://image.tmdb.org/t/p/original/followed-poster.jpg"), new MediaImage(ImageType.Fanart, "https://image.tmdb.org/t/p/original/followed-fanart.jpg")],
            }).Entity;
            await db.SaveChangesAsync();

            // A season poster is only the fallback: the series' own one wins.
            db.Add(new Bigarr.Domain.Tv.Season { SeriesId = followed.Id, SeasonNumber = 2, Images = [new MediaImage(ImageType.Poster, "https://image.tmdb.org/t/p/original/followed-s2.jpg")] });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Plex", kind = "plex", url = plex.Url, token = FakePlex.Token, enabled = true })).StatusCode);
        await plex.WaitForListenerAsync();
        plex.Set(new FakePlex.Stream("61", "s-61", "600", "7", "dana", "6001"));
        plex.Set(new FakePlex.Stream("71", "s-71", "700", "8", "eli", "7001"));
        plex.Set(new FakePlex.Stream("51", "s-51", "500", "9", "fred", "5001"));
        await plex.SendAsync("61", "600", "playing", 0);
        var now = await WaitUntilAsync(async () =>
        {
            var body = await (await session.GetAsync("/api/v1/playback/sessions")).ReadJsonAsync();
            return body.GetProperty("sessions").GetArrayLength() == 3 ? body : (JsonElement?)null;
        });

        var sessions = now.GetProperty("sessions").EnumerateArray().ToDictionary(s => s.GetProperty("user").GetProperty("name").GetString()!);
        Assert.Equal("https://image.tmdb.org/t/p/original/followed-poster.jpg", sessions["dana"].GetProperty("posterUrl").GetString());
        Assert.Equal("https://image.tmdb.org/t/p/original/followed-fanart.jpg", sessions["dana"].GetProperty("fanartUrl").GetString());
        Assert.Equal(JsonValueKind.Null, sessions["eli"].GetProperty("posterUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, sessions["eli"].GetProperty("fanartUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, sessions["fred"].GetProperty("posterUrl").ValueKind);

        // Nothing is fetched from Plex for pictures.
        Assert.DoesNotContain(plex.Requests, r => r.Path.Contains("/photo/", StringComparison.Ordinal) || r.Path.Contains("/thumb", StringComparison.Ordinal));
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
