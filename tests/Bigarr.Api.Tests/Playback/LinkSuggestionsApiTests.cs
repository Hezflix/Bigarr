using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>Suggestions through the API, TMDB played by the fixture provider the tests run with (no key).</summary>
public sealed class LinkSuggestionsApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task An_item_gets_its_films_from_tmdb_and_the_library_and_nothing_is_linked()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        int itemId;
        int matrixId;
        int episodeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var (matrix, _) = await TestData.SeedMovieAsync(db, tmdbId: 603);
            db.MovieVersions.Add(TestData.NewVersion(matrix, "4k"));
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex suggestions", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            // A French title the library does not know, but TMDB (the fixture's alternative title "Matrix") does.
            var item = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "gone-9", Kind = MediaItemKind.Movie, Title = "Matrix", OriginalTitle = "The Matrix", Year = 1999 }).Entity;
            var episode = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "ep-9", Kind = MediaItemKind.Episode, Title = "Pilot" }).Entity;
            await db.SaveChangesAsync();
            (itemId, matrixId, episodeId) = (item.Id, matrix.Id, episode.Id);
        }

        var response = await session.GetAsync($"/api/v1/playback/items/{itemId}/suggestions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.True(body.GetProperty("tmdbSearched").GetBoolean());
        var first = body.GetProperty("suggestions")[0];
        Assert.Equal(matrixId, first.GetProperty("movieId").GetInt32());
        Assert.Equal(2, first.GetProperty("versions").GetInt32());
        var reasons = first.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(["tmdbSearch", "titleAndYear"], reasons);
        Assert.Equal(95, first.GetProperty("score").GetInt32()); // 90 for TMDB's first hit of the year, 5 for the second reason

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Null((await db.MediaServerItems.SingleAsync(i => i.Id == itemId)).MovieId);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync("/api/v1/playback/items/999999/suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.GetAsync($"/api/v1/playback/items/{episodeId}/suggestions")).StatusCode);
    }
}
