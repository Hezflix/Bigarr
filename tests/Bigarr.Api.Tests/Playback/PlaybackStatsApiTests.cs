using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Playback;

/// <summary>The stats through the API: the library list's play counts, the Plays page's parameters, the user page, the backstop task.</summary>
public sealed class PlaybackStatsApiTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task The_library_list_counts_the_plays_of_each_film_and_version()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        int played;
        int unplayed;
        int hd;
        int uhd;
        var at = new DateTime(2026, 2, 1, 21, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var (movie, version) = await TestData.SeedMovieAsync(db, tmdbId: 990101);
            var second = TestData.NewVersion(movie, "4k");
            second.FolderName = "The Matrix (1999) 4K";
            db.MovieVersions.Add(second);
            var (other, _) = await TestData.SeedMovieAsync(db, tmdbId: 990102);
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex library counts", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "alice" }).Entity;
            var item = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "1", Title = "The Matrix", MovieId = movie.Id }).Entity;
            db.Plays.AddRange(
                new Play { User = user, Item = item, StartedAt = at.AddDays(-9), StoppedAt = at.AddDays(-9).AddHours(2), MovieId = movie.Id, VersionId = version.Id, VersionName = "1080p" },
                new Play { User = user, Item = item, StartedAt = at.AddHours(-2), StoppedAt = at, MovieId = movie.Id, VersionId = version.Id, VersionName = "1080p" },
                new Play { User = user, Item = item, StartedAt = at.AddDays(1), StoppedAt = at.AddDays(1).AddHours(2), MovieId = movie.Id });
            await db.SaveChangesAsync();
            (played, unplayed, hd, uhd) = (movie.Id, other.Id, version.Id, second.Id);
        }

        var list = await (await session.GetAsync("/api/v1/movies")).ReadJsonAsync();
        var film = list.EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == played);
        Assert.Equal(3, film.GetProperty("plays").GetInt32());
        Assert.Equal(at.AddDays(1).AddHours(2), film.GetProperty("lastPlayedAt").GetDateTime().ToUniversalTime());
        var versions = film.GetProperty("versions").EnumerateArray().ToDictionary(v => v.GetProperty("id").GetInt32());
        Assert.Equal(2, versions[hd].GetProperty("plays").GetInt32());
        Assert.Equal(at, versions[hd].GetProperty("lastPlayedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(0, versions[uhd].GetProperty("plays").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, versions[uhd].GetProperty("lastPlayedAt").ValueKind);

        var never = list.EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == unplayed);
        Assert.Equal(0, never.GetProperty("plays").GetInt32());
    }

    [Fact]
    public async Task The_plays_take_their_enums_in_camel_case_and_a_user_is_edited()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        int userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex user page", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            var erin = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "9", Name = "erin" }).Entity;
            await db.SaveChangesAsync();
            userId = erin.Id;
        }

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/playback/plays?kind=movie&decision=directPlay&from=2026-01-01&to=2026-02-01T00:00:00Z&version=4k&q=matrix")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/playback/plays?kind=Episode&decision=TRANSCODE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.GetAsync("/api/v1/playback/plays?kind=music")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.GetAsync("/api/v1/playback/plays?decision=3")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/playback/plays/versions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/v1/playback/stats?days=7&tz=Europe/Paris")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.GetAsync("/api/v1/playback/stats?days=0")).StatusCode);

        var page = await (await session.GetAsync($"/api/v1/playback/users/{userId}")).ReadJsonAsync();
        Assert.True(page.GetProperty("keepHistory").GetBoolean());
        Assert.Equal(4, page.GetProperty("watchTime").GetArrayLength());

        var edited = await session.PutAsJsonAsync($"/api/v1/playback/users/{userId}", new { displayName = "Erin (sister)", keepHistory = false });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await edited.ReadJsonAsync();
        Assert.Equal("Erin (sister)", body.GetProperty("displayName").GetString());
        Assert.False(body.GetProperty("keepHistory").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await session.GetAsync("/api/v1/playback/users/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PutAsJsonAsync("/api/v1/playback/users/999999", new { keepHistory = true })).StatusCode);
    }

    [Fact]
    public async Task The_plex_history_task_keeps_its_schedule_when_the_scheduler_is_disabled()
    {
        // The test host runs with Bigarr:Scheduler:Enabled = false, like shadow mode.
        var runner = factory.Services.GetRequiredService<TaskRunner>();
        var session = await CompatTestHelpers.SessionAsync(factory);
        var tasks = await (await session.GetAsync("/api/v1/system/tasks")).ReadJsonAsync();
        var history = tasks.EnumerateArray().Single(t => t.GetProperty("name").GetString() == PlexHistoryJob.JobName);
        Assert.True(history.GetProperty("enabled").GetBoolean());
        Assert.False(tasks.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "rssSync").GetProperty("enabled").GetBoolean());

        var due = runner.Due(DateTime.UtcNow.AddDays(2));
        Assert.Contains(PlexHistoryJob.JobName, due);
        Assert.DoesNotContain("rssSync", due);

        // By hand it runs like any task, here with no Plex server enabled.
        var run = await session.PostAsync($"/api/v1/system/tasks/{PlexHistoryJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
    }
}
