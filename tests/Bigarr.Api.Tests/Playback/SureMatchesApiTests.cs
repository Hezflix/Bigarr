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

/// <summary>The films not linked, a library and their plays, for the sure matches and the bulk link.</summary>
internal static class SureMatchesSeed
{
    public static readonly DateTime At = new(2026, 4, 2, 21, 0, 0, DateTimeKind.Utc);

    public sealed record Seeded(int UserId, int ChihiroItemId, int InceptionItemId, int UnknownItemId, int UnlinkedItemId, int SpiritedAwayId, int InceptionId);

    /// <summary>
    /// Two library films (TMDB played by the fixture provider): a French title of one of them, sure; a film a year off,
    /// not sure; a film Bigarr does not have; an item unlinked by hand; an episode, never counted.
    /// </summary>
    public static async Task<Seeded> SeedAsync(BigarrApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var (spirited, _) = await TestData.SeedMovieAsync(db, tmdbId: 129);
        spirited.Title = "Spirited Away";
        spirited.Year = 2001;
        var (inception, _) = await TestData.SeedMovieAsync(db, tmdbId: 27205);
        inception.Title = "Inception";
        inception.Year = 2010;
        var server = db.MediaServers.Add(new MediaServer { Name = "Plex sure", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
        await db.SaveChangesAsync();

        var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "7", Name = "dana" }).Entity;
        MediaServerItem Item(string key, MediaItemKind kind, string title, int? year, bool unlinked = false) =>
            db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = key, Kind = kind, Title = title, Year = year, LinkedByHand = unlinked }).Entity;
        var chihiro = Item("c-1", MediaItemKind.Movie, "Le Voyage de Chihiro", 2001);
        var offByOne = Item("c-2", MediaItemKind.Movie, "Inception", 2011);
        var unknown = Item("c-3", MediaItemKind.Movie, "A Film Bigarr Does Not Have", 2005);
        var unlinked = Item("c-4", MediaItemKind.Movie, "Le Voyage de Chihiro", 2001, unlinked: true);
        var episode = Item("c-5", MediaItemKind.Episode, "Pilot", 2001);
        var day = 0;
        foreach (var item in new[] { chihiro, chihiro, chihiro, offByOne, unknown, unlinked, episode })
        {
            var start = At.AddDays(day++);
            db.Plays.Add(new Play { User = user, Item = item, StartedAt = start, StoppedAt = start.AddHours(2), Source = PlaySource.Tautulli });
        }

        await db.SaveChangesAsync();
        return new Seeded(user.Id, chihiro.Id, offByOne.Id, unknown.Id, unlinked.Id, spirited.Id, inception.Id);
    }
}

/// <summary>The sure matches through the API: only the sure ones are offered, and nothing is linked by asking.</summary>
public sealed class SureMatchesApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Only_the_sure_matches_are_offered_and_nothing_is_linked()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var seeded = await SureMatchesSeed.SeedAsync(factory);

        var response = await session.GetAsync("/api/v1/playback/items/sure-matches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.True(body.GetProperty("tmdbSearched").GetBoolean());

        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());
        var item = match.GetProperty("item");
        Assert.Equal(seeded.ChihiroItemId, item.GetProperty("id").GetInt32());
        Assert.Equal("Le Voyage de Chihiro", item.GetProperty("title").GetString());
        Assert.Equal(2001, item.GetProperty("year").GetInt32());
        Assert.Equal(3, item.GetProperty("plays").GetInt32());
        var film = match.GetProperty("film");
        Assert.Equal(seeded.SpiritedAwayId, film.GetProperty("movieId").GetInt32());
        Assert.Equal("Spirited Away", film.GetProperty("title").GetString());
        Assert.Equal(1, film.GetProperty("versions").GetInt32());
        Assert.Equal(["tmdbSearch"], film.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        // The film a year off, the film Bigarr does not have and the item unlinked by hand; the episode is not a film.
        Assert.Equal(3, body.GetProperty("withoutSureMatch").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Null((await db.MediaServerItems.SingleAsync(i => i.Id == seeded.ChihiroItemId)).MovieId);
    }
}

/// <summary>Linking the confirmed pairs at once: every play follows, items linked meanwhile are skipped, counts follow.</summary>
public sealed class LinkManyApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task The_pairs_are_linked_with_all_their_plays_and_items_linked_meanwhile_are_skipped()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var seeded = await SureMatchesSeed.SeedAsync(factory);

        // Linked by hand to another film after the list was made: left as it is.
        Assert.Equal(HttpStatusCode.OK, (await session.PutAsJsonAsync($"/api/v1/playback/items/{seeded.UnknownItemId}/link", new { movieId = seeded.InceptionId })).StatusCode);

        var response = await session.PostAsJsonAsync("/api/v1/playback/items/links", new
        {
            links = new[]
            {
                new { itemId = seeded.ChihiroItemId, movieId = seeded.SpiritedAwayId },
                new { itemId = seeded.InceptionItemId, movieId = seeded.InceptionId },
                new { itemId = seeded.UnknownItemId, movieId = seeded.SpiritedAwayId },
                new { itemId = seeded.UnlinkedItemId, movieId = seeded.SpiritedAwayId },
                new { itemId = 999999, movieId = seeded.SpiritedAwayId },
            },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        var linked = body.GetProperty("linked").EnumerateArray().ToDictionary(l => l.GetProperty("itemId").GetInt32());
        Assert.Equal([seeded.ChihiroItemId, seeded.InceptionItemId], linked.Keys.Order());
        Assert.Equal(3, linked[seeded.ChihiroItemId].GetProperty("plays").GetInt32());
        Assert.True(linked[seeded.ChihiroItemId].GetProperty("linkedByHand").GetBoolean());
        Assert.Equal([seeded.UnknownItemId, seeded.UnlinkedItemId, 999999], body.GetProperty("skipped").EnumerateArray().Select(s => s.GetInt32()).Order());

        // Only the item unlinked by hand is left among the films not linked.
        var unmatched = await PlaysAsync(session, $"userId={seeded.UserId}&unmatched=true");
        Assert.Equal(1, unmatched.GetProperty("totalRecords").GetInt32());

        // The library list and the top films count the plays; the item linked meanwhile stays on its film.
        var films = (await (await session.GetAsync("/api/v1/movies")).ReadJsonAsync()).EnumerateArray().ToDictionary(m => m.GetProperty("id").GetInt32());
        Assert.Equal(3, films[seeded.SpiritedAwayId].GetProperty("plays").GetInt32());
        Assert.Equal(SureMatchesSeed.At.AddDays(2).AddHours(2), films[seeded.SpiritedAwayId].GetProperty("lastPlayedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(2, films[seeded.InceptionId].GetProperty("plays").GetInt32());
        var stats = await (await session.GetAsync($"/api/v1/playback/stats?userId={seeded.UserId}")).ReadJsonAsync();
        var top = stats.GetProperty("topMovies").EnumerateArray().First();
        Assert.Equal(seeded.SpiritedAwayId, top.GetProperty("movieId").GetInt32());
        Assert.Equal(3, top.GetProperty("plays").GetInt32());

        // The same pairs again: all skipped now.
        var again = await (await session.PostAsJsonAsync("/api/v1/playback/items/links", new { links = new[] { new { itemId = seeded.ChihiroItemId, movieId = seeded.SpiritedAwayId } } })).ReadJsonAsync();
        Assert.Empty(again.GetProperty("linked").EnumerateArray());
        Assert.Equal([seeded.ChihiroItemId], again.GetProperty("skipped").EnumerateArray().Select(s => s.GetInt32()));

        // A future play of a linked item is the film's too: the item keeps the link.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var item = await db.MediaServerItems.SingleAsync(i => i.Id == seeded.ChihiroItemId);
        Assert.True(item.LinkedByHand);
        Assert.Equal(seeded.SpiritedAwayId, item.MovieId);
    }

    private static async Task<JsonElement> PlaysAsync(HttpClient session, string query) =>
        await (await session.GetAsync("/api/v1/playback/plays?" + query)).ReadJsonAsync();
}
