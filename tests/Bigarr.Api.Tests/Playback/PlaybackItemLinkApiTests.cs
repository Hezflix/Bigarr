using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>A film play linked to no Bigarr film, linked by hand: the item's plays follow, and every count with them.</summary>
public sealed class PlaybackItemLinkApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly DateTime At = new(2026, 4, 2, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Linking_an_item_moves_all_its_plays_to_the_film_and_the_counts_follow()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var seeded = await SeedAsync(990201, 990202);

        var unmatched = await PlaysAsync(session, $"userId={seeded.UserId}&unmatched=true");
        Assert.Equal(2, unmatched.GetProperty("totalRecords").GetInt32());
        var first = unmatched.GetProperty("records")[0].GetProperty("item");
        Assert.Equal("La Matrice", first.GetProperty("serverTitle").GetString());
        Assert.False(first.GetProperty("linkedByHand").GetBoolean());

        var linked = await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = seeded.MovieId, versionId = seeded.VersionId });
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var body = await linked.ReadJsonAsync();
        Assert.Equal(2, body.GetProperty("plays").GetInt32());
        Assert.True(body.GetProperty("linkedByHand").GetBoolean());

        Assert.Equal(0, (await PlaysAsync(session, $"userId={seeded.UserId}&unmatched=true")).GetProperty("totalRecords").GetInt32());
        var plays = await PlaysAsync(session, $"userId={seeded.UserId}&movieId={seeded.MovieId}");
        Assert.Equal(2, plays.GetProperty("totalRecords").GetInt32());
        var record = plays.GetProperty("records")[0];
        Assert.Equal("1080p", record.GetProperty("versionName").GetString());
        Assert.Equal("The Matrix", record.GetProperty("item").GetProperty("title").GetString());
        Assert.Equal("La Matrice", record.GetProperty("item").GetProperty("serverTitle").GetString());

        // Found under either title.
        Assert.Equal(2, (await PlaysAsync(session, $"userId={seeded.UserId}&q=matrix")).GetProperty("totalRecords").GetInt32());
        Assert.Equal(2, (await PlaysAsync(session, $"userId={seeded.UserId}&q=matrice")).GetProperty("totalRecords").GetInt32());

        // The library list, the film's plays per version and the top films all count them.
        var film = (await (await session.GetAsync("/api/v1/movies")).ReadJsonAsync()).EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == seeded.MovieId);
        Assert.Equal(2, film.GetProperty("plays").GetInt32());
        Assert.Equal(At.AddDays(1).AddHours(2), film.GetProperty("lastPlayedAt").GetDateTime().ToUniversalTime());
        var versions = await (await session.GetAsync($"/api/v1/playback/movies/{seeded.MovieId}")).ReadJsonAsync();
        Assert.Equal(2, versions.EnumerateArray().Single(v => v.GetProperty("versionId").ValueKind == JsonValueKind.Number && v.GetProperty("versionId").GetInt32() == seeded.VersionId).GetProperty("plays").GetInt32());
        var stats = await (await session.GetAsync($"/api/v1/playback/stats?userId={seeded.UserId}")).ReadJsonAsync();
        var top = stats.GetProperty("topMovies").EnumerateArray().Single();
        Assert.Equal(seeded.MovieId, top.GetProperty("movieId").GetInt32());
        Assert.Equal(2, top.GetProperty("plays").GetInt32());

        // The item keeps the link: a play recorded later from it is the film's too.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var item = await db.MediaServerItems.SingleAsync(i => i.Id == seeded.ItemId);
            Assert.True(item.LinkedByHand);
            Assert.Equal(seeded.MovieId, item.MovieId);
            Assert.Equal(seeded.VersionId, item.LinkedVersionId);
        }
    }

    [Fact]
    public async Task A_wrong_link_is_moved_to_another_film_or_undone()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var seeded = await SeedAsync(990211, 990212);
        Assert.Equal(HttpStatusCode.OK, (await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = seeded.MovieId, versionId = seeded.VersionId })).StatusCode);

        // Relinked to the other film without a version: the first film's version goes with it.
        var moved = await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = seeded.OtherMovieId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var plays = await PlaysAsync(session, $"userId={seeded.UserId}&movieId={seeded.OtherMovieId}");
        Assert.Equal(2, plays.GetProperty("totalRecords").GetInt32());
        Assert.All(plays.GetProperty("records").EnumerateArray(), p => Assert.Equal(JsonValueKind.Null, p.GetProperty("versionName").ValueKind));
        Assert.Equal(0, (await PlaysAsync(session, $"userId={seeded.UserId}&movieId={seeded.MovieId}")).GetProperty("totalRecords").GetInt32());

        var undone = await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = (int?)null });
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        var unmatched = await PlaysAsync(session, $"userId={seeded.UserId}&unmatched=true");
        Assert.Equal(2, unmatched.GetProperty("totalRecords").GetInt32());
        Assert.Equal("La Matrice", unmatched.GetProperty("records")[0].GetProperty("item").GetProperty("title").GetString());
        Assert.True(unmatched.GetProperty("records")[0].GetProperty("item").GetProperty("linkedByHand").GetBoolean());
    }

    [Fact]
    public async Task A_link_needs_a_film_item_an_existing_film_and_one_of_its_versions()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var seeded = await SeedAsync(990221, 990222);
        int episodeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var serverId = await db.MediaServerItems.Where(i => i.Id == seeded.ItemId).Select(i => i.MediaServerId).SingleAsync();
            var episode = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = serverId, RatingKey = "ep-1", Kind = MediaItemKind.Episode, Title = "Pilot" }).Entity;
            await db.SaveChangesAsync();
            episodeId = episode.Id;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync("/api/v1/playback/items/999999/link", new { movieId = seeded.MovieId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync($"/api/v1/playback/items/{episodeId}/link", new { movieId = seeded.MovieId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = 999999 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = seeded.OtherMovieId, versionId = seeded.VersionId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.ItemId}/link", new { movieId = (int?)null, versionId = seeded.VersionId })).StatusCode);
    }

    private sealed record Seeded(int UserId, int ItemId, int MovieId, int VersionId, int OtherMovieId);

    /// <summary>A film the server once played under a title of its own (an import), two plays of it, and two Bigarr films.</summary>
    private async Task<Seeded> SeedAsync(int tmdbId, int otherTmdbId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var (movie, version) = await TestData.SeedMovieAsync(db, tmdbId: tmdbId);
        var (other, _) = await TestData.SeedMovieAsync(db, tmdbId: otherTmdbId);
        var server = db.MediaServers.Add(new MediaServer { Name = $"Plex links {tmdbId}", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
        await db.SaveChangesAsync();
        var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "7", Name = $"dana{tmdbId}" }).Entity;
        var item = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "gone-1", Kind = MediaItemKind.Movie, Title = "La Matrice", Year = 1999 }).Entity;
        db.Plays.AddRange(
            new Play { User = user, Item = item, StartedAt = At, StoppedAt = At.AddHours(2), Source = PlaySource.Tautulli },
            new Play { User = user, Item = item, StartedAt = At.AddDays(1), StoppedAt = At.AddDays(1).AddHours(2), Source = PlaySource.Tautulli });
        await db.SaveChangesAsync();
        return new Seeded(user.Id, item.Id, movie.Id, version.Id, other.Id);
    }

    private static async Task<JsonElement> PlaysAsync(HttpClient session, string query) =>
        await (await session.GetAsync("/api/v1/playback/plays?" + query)).ReadJsonAsync();
}
