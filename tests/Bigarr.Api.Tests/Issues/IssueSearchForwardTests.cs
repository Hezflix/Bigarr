using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Issues;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Api.Tests.Requests;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Issues;

/// <summary>
/// "Search again" while a followed Radarr/Sonarr carries the issue's version (shadow mode): Bigarr still records its pick,
/// and the app gets its own search command for the film, the series, the season or the episode. A refusal is said on the
/// issue line and breaks nothing. Against fake Radarr and Sonarr servers.
/// </summary>
public sealed class IssueSearchForwardTests
{
    private const string Key = "forward-secret-key-0123456789";

    [Fact]
    public async Task A_film_search_goes_to_the_followed_radarr_as_a_movies_search()
    {
        await using var env = await StartAsync(shadow: true);
        var film = await env.FilmInLibraryAsync(withGrab: true);
        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/movie") => new FakeHttpServer.Answer(200, """[{ "id": 41, "title": "The Matrix", "tmdbId": 603 }]"""),
            ("POST", "/api/v3/command") => new FakeHttpServer.Answer(201, """{ "id": 1 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, film.VersionName, 7, await env.HdAsync(), "/radarr-movies", env.FilmRootId);
        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var manager = await env.UserAsync("demo-manager", UserPermissions.Defaults(), UserRole.RequestManager);
        var issue = await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks"));

        // The file actions stay off, the search is offered and names the app.
        var before = await GetAsync(env.Admin, issue.Id);
        Assert.Equal($"Demo Radarr {film.VersionName}", before.Actions!.ManagedBy);
        Assert.True(before.Actions.Search);
        Assert.False(before.Actions.Blocklist);

        var line = (await SearchAsync(env.Admin, issue.Id)).Comments!.Single(c => c.Kind == IssueCommentKind.Action);
        Assert.Contains($"sent to Radarr 'Demo Radarr {film.VersionName}'", line.Outcome, StringComparison.Ordinal);
        Assert.Contains("MoviesSearch for movie 41", line.Detail, StringComparison.Ordinal);

        Assert.Equal("?tmdbId=603", radarr.Requests.Single(r => r.Method == "GET").Query);
        var post = radarr.Requests.Single(r => r.Method == "POST");
        Assert.Equal(Key, post.Headers["X-Api-Key"]);
        using var body = JsonDocument.Parse(post.Body);
        Assert.Equal("MoviesSearch", body.RootElement.GetProperty("name").GetString());
        Assert.Equal([41], body.RootElement.GetProperty("movieIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(2, radarr.Requests.Count);

        // Bigarr's own search still ran (a pick in shadow mode); the manager reads the outcome without the details.
        Assert.Contains("Searched again", line.Outcome, StringComparison.Ordinal);
        var managerLine = (await GetAsync(manager, issue.Id)).Comments!.Single(c => c.Kind == IssueCommentKind.Action);
        Assert.Equal(line.Outcome, managerLine.Outcome);
        Assert.Null(managerLine.Detail);
    }

    [Fact]
    public async Task Episode_season_and_series_searches_go_to_the_followed_sonarr_with_its_ids()
    {
        await using var env = await StartAsync(shadow: true);
        var (versionId, versionName) = await env.SeriesInLibraryAsync();
        await env.GiveFilesAsync(versionId, 1);
        var tvdbId = await env.DbAsync(db => db.Series.Where(s => s.TmdbId == 1396).Select(s => s.TvdbId!.Value).SingleAsync());
        await using var sonarr = await FakeHttpServer.StartAsync();
        sonarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/series") => new FakeHttpServer.Answer(200, """[{ "id": 17, "title": "Breaking Bad" }]"""),
            ("GET", "/api/v3/episode") => new FakeHttpServer.Answer(200, """[{ "id": 301, "seasonNumber": 1, "episodeNumber": 1 }, { "id": 302, "seasonNumber": 1, "episodeNumber": 2 }]"""),
            ("POST", "/api/v3/command") => new FakeHttpServer.Answer(201, """{ "id": 1 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowSonarrAsync(sonarr.Url, versionName, 7, await env.SeriesHdAsync(), "/tv", env.SeriesRootId);
        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var episodeId = await env.DbAsync(db => db.Episodes.Where(e => e.Series.TmdbId == 1396 && e.SeasonNumber == 1 && e.EpisodeNumber == 2).Select(e => e.Id).SingleAsync());

        var episode = await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Audio, IssueScope.Episode, "No sound", EpisodeId: episodeId));
        var season = await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Subtitles, IssueScope.Season, "Subtitles drift", SeasonNumber: 1));
        var series = await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Other, IssueScope.Series, "Wrong order"));

        await SearchAsync(env.Admin, episode.Id);
        await SearchAsync(env.Admin, season.Id);
        var last = await SearchAsync(env.Admin, series.Id);
        Assert.Contains($"sent to Sonarr 'Demo Sonarr {versionName}'", last.Comments!.Single(c => c.Kind == IssueCommentKind.Action).Outcome, StringComparison.Ordinal);

        Assert.All(sonarr.Requests.Where(r => r.Path == "/api/v3/series"), r => Assert.Equal($"?tvdbId={tvdbId}", r.Query));
        Assert.Equal("?seriesId=17", sonarr.Requests.Single(r => r.Path == "/api/v3/episode").Query);
        var commands = sonarr.Requests.Where(r => r.Method == "POST").Select(r => JsonDocument.Parse(r.Body).RootElement).ToList();
        Assert.Equal(3, commands.Count);
        Assert.Equal("EpisodeSearch", commands[0].GetProperty("name").GetString());
        Assert.Equal([302], commands[0].GetProperty("episodeIds").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal("SeasonSearch", commands[1].GetProperty("name").GetString());
        Assert.Equal(17, commands[1].GetProperty("seriesId").GetInt32());
        Assert.Equal(1, commands[1].GetProperty("seasonNumber").GetInt32());
        Assert.Equal("SeriesSearch", commands[2].GetProperty("name").GetString());
        Assert.Equal(17, commands[2].GetProperty("seriesId").GetInt32());
    }

    [Fact]
    public async Task A_followed_app_that_refuses_or_cannot_be_reached_is_said_on_the_line_and_breaks_nothing()
    {
        await using var env = await StartAsync(shadow: true);
        var film = await env.FilmInLibraryAsync(withGrab: true);
        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/movie") => new FakeHttpServer.Answer(200, """[{ "id": 41 }]"""),
            ("POST", "/api/v3/command") => new FakeHttpServer.Answer(503, """{ "message": "Database is being migrated" }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, film.VersionName, 7, await env.HdAsync(), "/radarr-movies", env.FilmRootId);
        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());
        var issue = await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks"));

        var refused = (await SearchAsync(env.Admin, issue.Id)).Comments!.Single(c => c.Kind == IssueCommentKind.Action);
        Assert.Contains("Sending the search to Radarr", refused.Outcome, StringComparison.Ordinal);
        Assert.Contains("failed", refused.Outcome, StringComparison.Ordinal);
        Assert.Contains("503", refused.Detail, StringComparison.Ordinal);

        // Unreachable: the same, and the issue is still usable (comment, resolve).
        await env.DbAsync(async db =>
        {
            var follow = await db.FollowedRadarrs.SingleAsync();
            follow.Url = "http://127.0.0.1:9";
            return await db.SaveChangesAsync();
        });
        var unreachable = (await SearchAsync(env.Admin, issue.Id)).Comments!.Where(c => c.Kind == IssueCommentKind.Action).Last();
        Assert.Contains("failed", unreachable.Outcome, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.PostAsync($"/api/v1/issues/{issue.Id}/resolve", null)).StatusCode);
    }

    private static async Task<IssueResponse> ReportAsync(HttpClient client, CreateIssueRequest body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/issues", body, Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
    }

    private static async Task<IssueResponse> GetAsync(HttpClient client, int id) => (await client.GetFromJsonAsync<IssueResponse>($"/api/v1/issues/{id}", Json))!;

    private static async Task<IssueResponse> SearchAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/issues/{id}/search", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
    }
}
