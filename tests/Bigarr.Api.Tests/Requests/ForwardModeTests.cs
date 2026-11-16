using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Requests;

/// <summary>
/// Forward mode: while Bigarr follows a Radarr or Sonarr for a version, an approved request of that version is also added
/// there, with the payload Seerr sends; a title already there counts as forwarded; a failure is shown and retried, and
/// never loses the request. Against fake Radarr and Sonarr servers.
/// </summary>
public sealed class ForwardModeTests
{
    private const string Key = "forward-secret-key-0123456789";

    [Fact]
    public async Task An_approved_film_is_added_to_the_followed_radarr_of_its_version_as_seerr_would()
    {
        await using var env = await StartAsync();
        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/movie/lookup/tmdb") => new FakeHttpServer.Answer(200, """{ "title": "The Matrix", "year": 1999, "tmdbId": 603, "id": 0 }"""),
            ("POST", "/api/v3/movie") => new FakeHttpServer.Answer(201, """{ "id": 41, "title": "The Matrix" }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, "1080p", profile: 7, bigarrProfile: await env.HdAsync(), root: "/radarr-movies", bigarrRoot: env.FilmRootId);
        // A Radarr followed for another version is left alone.
        await env.FollowRadarrAsync("http://127.0.0.1:9", "4k", profile: 9, bigarrProfile: await env.HdAsync(), root: "/radarr4k", bigarrRoot: env.FilmRootId);

        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var manager = await env.UserAsync("demo-manager", UserPermissions.Defaults(), UserRole.RequestManager);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 603);
        var approved = await env.ApproveAsync(manager, asked.Id);
        Assert.Equal(ForwardStatus.Forwarded, approved.ForwardStatus);
        Assert.Equal("Demo Radarr 1080p", approved.ForwardedTo);

        var post = radarr.Requests.Single(r => r.Method == "POST");
        Assert.Equal(Key, post.Headers["X-Api-Key"]);
        using var body = JsonDocument.Parse(post.Body);
        var root = body.RootElement;
        Assert.Equal(603, root.GetProperty("tmdbId").GetInt32());
        Assert.Equal("The Matrix", root.GetProperty("title").GetString());
        Assert.Equal(1999, root.GetProperty("year").GetInt32());
        Assert.Equal(7, root.GetProperty("qualityProfileId").GetInt32());
        Assert.Equal(7, root.GetProperty("profileId").GetInt32());
        Assert.Equal("/radarr-movies", root.GetProperty("rootFolderPath").GetString());
        Assert.Equal("released", root.GetProperty("minimumAvailability").GetString());
        Assert.True(root.GetProperty("monitored").GetBoolean());
        Assert.True(root.GetProperty("addOptions").GetProperty("searchForMovie").GetBoolean());

        // Bigarr has it too, and the requester sees nothing of the forward.
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 603)));
        Assert.Null((await GetAsync(user, asked.Id)).ForwardStatus);
    }

    [Fact]
    public async Task A_film_radarr_already_has_counts_as_forwarded_and_is_monitored_and_searched_there()
    {
        await using var env = await StartAsync();
        await using var radarr = await FakeHttpServer.StartAsync();
        const string existing = """{ "id": 12, "title": "The Matrix", "year": 1999, "tmdbId": 603, "monitored": false, "qualityProfileId": 7, "path": "/radarr-movies/The Matrix (1999)" }""";
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/movie/lookup/tmdb") => new FakeHttpServer.Answer(200, existing),
            ("GET", "/api/v3/movie/12") => new FakeHttpServer.Answer(200, existing),
            ("PUT", "/api/v3/movie/12") => new FakeHttpServer.Answer(202, existing),
            ("POST", "/api/v3/command") => new FakeHttpServer.Answer(201, """{ "id": 1 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, "1080p", 7, await env.HdAsync(), "/radarr-movies", env.FilmRootId);

        var asked = await AskOkAsync(env.Admin, RequestMediaType.Movie, 603); // the admin's own request: approved at once
        var seen = await GetAsync(env.Admin, asked.Id);
        Assert.Equal(ForwardStatus.AlreadyThere, seen.ForwardStatus);
        Assert.DoesNotContain(radarr.Requests, r => r.Method == "POST" && r.Path == "/api/v3/movie");
        using var put = JsonDocument.Parse(radarr.Requests.Single(r => r.Method == "PUT").Body);
        Assert.True(put.RootElement.GetProperty("monitored").GetBoolean());
        Assert.Equal("/radarr-movies/The Matrix (1999)", put.RootElement.GetProperty("path").GetString()); // the resource goes back whole
        using var command = JsonDocument.Parse(radarr.Requests.Single(r => r.Path == "/api/v3/command").Body);
        Assert.Equal("MoviesSearch", command.RootElement.GetProperty("name").GetString());
        Assert.Equal(12, command.RootElement.GetProperty("movieIds")[0].GetInt32());
    }

    [Fact]
    public async Task A_failed_forward_keeps_the_approved_request_shows_the_error_and_is_retried()
    {
        await using var env = await StartAsync();
        await using var radarr = await FakeHttpServer.StartAsync();
        var down = true;
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            _ when down => new FakeHttpServer.Answer(500, """{ "message": "database is locked" }"""),
            ("GET", "/api/v3/movie/lookup/tmdb") => new FakeHttpServer.Answer(200, """{ "title": "The Matrix", "year": 1999, "tmdbId": 603 }"""),
            ("POST", "/api/v3/movie") => new FakeHttpServer.Answer(201, """{ "id": 41 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, "1080p", 7, await env.HdAsync(), "/radarr-movies", env.FilmRootId);
        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var manager = await env.UserAsync("demo-manager", UserPermissions.Defaults(), UserRole.RequestManager);
        var asked = await AskOkAsync(user, RequestMediaType.Movie, 603);

        var approved = await env.ApproveAsync(env.Admin, asked.Id);
        Assert.Equal(RequestStatus.Approved, approved.Status);
        Assert.Equal(ForwardStatus.Failed, approved.ForwardStatus);
        Assert.Contains("HTTP 500", approved.ForwardError, StringComparison.Ordinal);
        Assert.Contains("database is locked", approved.ForwardError, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, approved.ForwardError, StringComparison.Ordinal);
        Assert.True(await env.DbAsync(db => db.Movies.AnyAsync(m => m.TmdbId == 603))); // Bigarr took it all the same
        Assert.Contains("requestFailed", await env.OutboxTypesAsync());

        // The manager sees that it failed and may retry; the error text (it can carry an address) is the admin's.
        var managed = await GetAsync(manager, asked.Id);
        Assert.Equal(ForwardStatus.Failed, managed.ForwardStatus);
        Assert.Null(managed.ForwardError);
        Assert.Equal(1, (await manager.GetFromJsonAsync<RequestCountsResponse>("/api/v1/requests/count", Json))!.Failed);
        Assert.Single((await manager.GetFromJsonAsync<MediaRequestPage>("/api/v1/requests?filter=failed", Json))!.Records);

        down = false;
        var retried = await manager.PostAsync($"/api/v1/requests/{asked.Id}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(ForwardStatus.Forwarded, (await retried.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!.ForwardStatus);
        Assert.Equal(0, (await manager.GetFromJsonAsync<RequestCountsResponse>("/api/v1/requests/count", Json))!.Failed);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"/api/v1/requests/{asked.Id}/retry", null)).StatusCode);
    }

    [Fact]
    public async Task An_unmapped_profile_fails_the_forward_with_a_message_instead_of_guessing()
    {
        await using var env = await StartAsync();
        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = _ => new FakeHttpServer.Answer(500);
        await env.FollowRadarrAsync(radarr.Url, "1080p", 7, bigarrProfile: 999, "/radarr-movies", env.FilmRootId);
        var asked = await AskOkAsync(env.Admin, RequestMediaType.Movie, 603);
        var seen = await GetAsync(env.Admin, asked.Id);
        Assert.Equal(ForwardStatus.Failed, seen.ForwardStatus);
        Assert.Contains("quality profile", seen.ForwardError, StringComparison.Ordinal);
        Assert.Empty(radarr.Requests);
    }

    [Fact]
    public async Task Approved_seasons_go_to_the_followed_sonarr_with_only_those_seasons_monitored()
    {
        await using var env = await StartAsync();
        await using var sonarr = await FakeHttpServer.StartAsync();
        sonarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/series/lookup") => new FakeHttpServer.Answer(200, """[{ "title": "Breaking Bad", "tvdbId": 81189, "seasons": [{ "seasonNumber": 0, "monitored": false }, { "seasonNumber": 1, "monitored": false }, { "seasonNumber": 2, "monitored": false }, { "seasonNumber": 3, "monitored": false }] }]"""),
            ("GET", "/api/v3/languageprofile") => new FakeHttpServer.Answer(404), // Sonarr v4
            ("POST", "/api/v3/series") => new FakeHttpServer.Answer(201, """{ "id": 5 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowSonarrAsync(sonarr.Url, "1080p", 4, await env.SeriesHdAsync(), "/sonarr-tv", env.SeriesRootId);

        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var asked = await AskOkAsync(user, RequestMediaType.Series, 1396, seasons: [1, 2]);
        var approved = await env.ApproveAsync(env.Admin, asked.Id);
        Assert.Equal(ForwardStatus.Forwarded, approved.ForwardStatus);

        var post = sonarr.Requests.Single(r => r.Method == "POST");
        Assert.Contains("term=tvdb:81189", sonarr.Requests.Single(r => r.Path == "/api/v3/series/lookup").Query, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(post.Body);
        var root = body.RootElement;
        Assert.Equal(81189, root.GetProperty("tvdbId").GetInt32());
        Assert.Equal(4, root.GetProperty("qualityProfileId").GetInt32());
        Assert.Equal("/sonarr-tv", root.GetProperty("rootFolderPath").GetString());
        Assert.Equal("standard", root.GetProperty("seriesType").GetString());
        Assert.True(root.GetProperty("monitored").GetBoolean());
        Assert.True(root.GetProperty("seasonFolder").GetBoolean());
        Assert.True(root.GetProperty("addOptions").GetProperty("searchForMissingEpisodes").GetBoolean());
        Assert.False(root.TryGetProperty("languageProfileId", out _));
        var monitored = root.GetProperty("seasons").EnumerateArray().Where(s => s.GetProperty("monitored").GetBoolean()).Select(s => s.GetProperty("seasonNumber").GetInt32());
        Assert.Equal([1, 2], monitored);
    }

    [Fact]
    public async Task A_series_sonarr_already_has_gets_the_requested_seasons_monitored_and_a_search()
    {
        await using var env = await StartAsync();
        await using var sonarr = await FakeHttpServer.StartAsync();
        const string existing = """{ "id": 5, "title": "Breaking Bad", "tvdbId": 81189, "monitored": true, "path": "/sonarr-tv/Breaking Bad", "seasons": [{ "seasonNumber": 1, "monitored": true }, { "seasonNumber": 2, "monitored": false }, { "seasonNumber": 3, "monitored": false }] }""";
        sonarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/series/lookup") => new FakeHttpServer.Answer(200, "[" + existing + "]"),
            ("GET", "/api/v3/series/5") => new FakeHttpServer.Answer(200, existing),
            ("PUT", "/api/v3/series/5") => new FakeHttpServer.Answer(202, existing),
            ("POST", "/api/v3/command") => new FakeHttpServer.Answer(201, """{ "id": 1 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowSonarrAsync(sonarr.Url, "1080p", 4, await env.SeriesHdAsync(), "/sonarr-tv", env.SeriesRootId);

        var asked = await AskOkAsync(env.Admin, RequestMediaType.Series, 1396, seasons: [2]);
        Assert.Equal(ForwardStatus.AlreadyThere, (await GetAsync(env.Admin, asked.Id)).ForwardStatus);
        using var put = JsonDocument.Parse(sonarr.Requests.Single(r => r.Method == "PUT").Body);
        var seasons = put.RootElement.GetProperty("seasons").EnumerateArray().ToDictionary(s => s.GetProperty("seasonNumber").GetInt32(), s => s.GetProperty("monitored").GetBoolean());
        Assert.Equal(new Dictionary<int, bool> { [1] = true, [2] = true, [3] = false }, seasons);
        using var command = JsonDocument.Parse(sonarr.Requests.Single(r => r.Path == "/api/v3/command").Body);
        Assert.Equal("SeriesSearch", command.RootElement.GetProperty("name").GetString());
        Assert.Equal(5, command.RootElement.GetProperty("seriesId").GetInt32());
    }

    [Fact]
    public async Task Without_a_followed_app_nothing_is_forwarded()
    {
        await using var env = await StartAsync();
        var asked = await AskOkAsync(env.Admin, RequestMediaType.Movie, 603);
        Assert.Equal(ForwardStatus.None, (await GetAsync(env.Admin, asked.Id)).ForwardStatus);
    }
}

internal static class FollowedApps
{
    public static Task FollowRadarrAsync(this RequestsEnv env, string url, string version, int profile, int bigarrProfile, string root, int bigarrRoot) => env.DbAsync(db =>
    {
        db.FollowedRadarrs.Add(new FollowedRadarr
        {
            Name = $"Demo Radarr {version}",
            Url = url,
            ApiKey = "forward-secret-key-0123456789",
            VersionName = version,
            ProfileMap = new() { [profile] = bigarrProfile },
            RootFolderMap = new() { [root] = bigarrRoot },
        });
        return db.SaveChangesAsync();
    });

    public static Task FollowSonarrAsync(this RequestsEnv env, string url, string version, int profile, int bigarrProfile, string root, int bigarrRoot) => env.DbAsync(db =>
    {
        db.FollowedSonarrs.Add(new FollowedSonarr
        {
            Name = $"Demo Sonarr {version}",
            Url = url,
            ApiKey = "forward-secret-key-0123456789",
            VersionName = version,
            ProfileMap = new() { [profile] = bigarrProfile },
            RootFolderMap = new() { [root] = bigarrRoot },
        });
        return db.SaveChangesAsync();
    });
}
