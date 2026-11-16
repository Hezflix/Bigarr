using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Issues;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Api.Tests.Requests;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Auth;
using Bigarr.Domain.History;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Issues;

/// <summary>
/// Issues from report to resolved: scopes (film, series, season, episode), the version and file kept at report time, the
/// thread, resolve and reopen, the approvers' actions (search, blocklist, delete the file) and how shadow mode holds them
/// back, who sees what. Demo data only.
/// </summary>
public sealed class IssuesTests
{
    private const string Release = "The.Matrix.1999.1080p.BluRay.x264-DEMO";
    private const string FilmPath = "The Matrix (1999) Bluray-1080p.mkv";

    private static readonly UserPermissions Defaults = UserPermissions.Defaults();

    [Fact]
    public async Task A_user_reports_bad_audio_on_an_episode_the_admin_replies_searches_again_and_resolves()
    {
        await using var env = await StartAsync();
        var (versionId, _) = await env.SeriesInLibraryAsync();
        await env.GiveFilesAsync(versionId, 1);
        var user = await env.UserAsync("demo-friend", Defaults);

        // The report options: the version, the seasons and their episodes, which have files.
        var title = (await user.GetFromJsonAsync<IssueTitleResponse>("/api/v1/issues/title?mediaType=series&tmdbId=1396", Json))!;
        Assert.True(title.InLibrary);
        Assert.True(title.CanReport);
        Assert.Single(title.Versions);
        var episode = title.Seasons.Single(s => s.SeasonNumber == 1).Episodes.Single(e => e.EpisodeNumber == 2);
        Assert.True(episode.HasFile);

        var issue = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Audio, IssueScope.Episode, "No sound after ten minutes", EpisodeId: episode.Id));
        Assert.Equal(IssueStatus.Open, issue.Status);
        Assert.Equal(1, issue.SeasonNumber);
        Assert.StartsWith("S01E02", issue.EpisodeLabel, StringComparison.Ordinal);
        Assert.True(issue.Mine);
        Assert.True(issue.CanResolve);

        // The reporter sees the version, never the file, the release or the actions.
        Assert.Null(issue.File);
        Assert.Null(issue.Actions);
        Assert.Null(issue.LibraryId);

        // The admin sees which file it is, replies, searches again from the issue.
        var adminView = await GetIssueAsync(env.Admin, issue.Id);
        Assert.Equal("Season 1/E2.mkv", adminView.File!.Path);
        Assert.True(adminView.Actions!.Search);
        Assert.True(adminView.Actions.DeleteFile);
        Assert.False(adminView.Actions.Blocklist); // no release known for a scanned file
        await CommentOkAsync(env.Admin, issue.Id, "Looking for another release.");
        var searched = await PostOkAsync(env.Admin, $"/api/v1/issues/{issue.Id}/search");
        var action = searched.Comments!.Single(c => c.Kind == IssueCommentKind.Action);
        Assert.Equal(IssueAction.Search, action.Action);
        Assert.Equal("admin", action.Author!.Username);
        Assert.Contains("nothing suitable", action.Outcome, StringComparison.Ordinal);
        Assert.NotNull(action.Detail);

        // The reporter reads the thread: the reply, the action's outcome without its details; resolves it, then reopens it.
        var mine = await GetIssueAsync(user, issue.Id);
        Assert.Equal(["Comment", "Action"], mine.Comments!.Select(c => c.Kind.ToString()));
        Assert.Null(mine.Comments![1].Detail);
        var resolved = await PostOkAsync(user, $"/api/v1/issues/{issue.Id}/resolve");
        Assert.Equal(IssueStatus.Resolved, resolved.Status);
        Assert.Equal("demo-friend", resolved.ResolvedBy!.Username);
        Assert.True(resolved.CanReopen);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsync($"/api/v1/issues/{issue.Id}/resolve", null)).StatusCode);
        var reopened = await PostOkAsync(user, $"/api/v1/issues/{issue.Id}/reopen");
        Assert.Equal(IssueStatus.Open, reopened.Status);
        Assert.Null(reopened.ResolvedBy);
        Assert.Equal(["Comment", "Action", "Resolved", "Reopened"], reopened.Comments!.Select(c => c.Kind.ToString()));

        var events = await env.OutboxTypesAsync();
        Assert.Equal(1, events.Count(e => e == "issueCreated"));
        Assert.Equal(1, events.Count(e => e == "issueCommented"));
        Assert.Equal(1, events.Count(e => e == "issueResolved"));
        Assert.Equal(1, events.Count(e => e == "issueReopened"));
    }

    [Fact]
    public async Task Scopes_film_series_season_and_episode_keep_their_version_and_file()
    {
        await using var env = await StartAsync();
        var film = await env.FilmInLibraryAsync(withGrab: true);
        var (seriesVersionId, seriesVersionName) = await env.SeriesInLibraryAsync();
        await env.GiveFilesAsync(seriesVersionId, 1);
        var user = await env.UserAsync("demo-friend", Defaults);

        // A film: its default version, its file and the release that produced it, for the admin.
        var movie = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks all over"));
        Assert.Equal(film.VersionName, movie.VersionName);
        var adminMovie = await GetIssueAsync(env.Admin, movie.Id);
        Assert.Equal(FilmPath, adminMovie.File!.Path);
        Assert.Equal(Release, adminMovie.File.ReleaseTitle);
        Assert.Equal("Demo indexer", adminMovie.File.IndexerName);
        Assert.Equal(film.MovieId, adminMovie.LibraryId);
        Assert.True(adminMovie.Actions!.Blocklist);

        // A whole series, a season: the version, no file.
        var series = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Subtitles, IssueScope.Series, "Subtitles out of sync everywhere"));
        Assert.Equal(seriesVersionName, series.VersionName);
        Assert.Null((await GetIssueAsync(env.Admin, series.Id)).File);
        var season = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Other, IssueScope.Season, "Episodes in the wrong order", SeasonNumber: 1));
        Assert.Equal(1, season.SeasonNumber);
        Assert.Null(season.EpisodeLabel);
        var seasonAdmin = await GetIssueAsync(env.Admin, season.Id);
        Assert.False(seasonAdmin.Actions!.DeleteFile);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsJsonAsync($"/api/v1/issues/{season.Id}/delete-file", new IssueDeleteFileRequest(), Json)).StatusCode);

        // Refused: a scope that does not fit the title, an unknown season, version or episode, an empty message, a title
        // that is not in the library.
        Assert.Equal(HttpStatusCode.BadRequest, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Season, "x", SeasonNumber: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Video, IssueScope.Season, "x", SeasonNumber: 9))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Series, 1396, IssueType.Video, IssueScope.Episode, "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "x", VersionName: "nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "   "))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 604, IssueType.Video, IssueScope.Movie, "x"))).StatusCode);
        var notInLibrary = (await user.GetFromJsonAsync<IssueTitleResponse>("/api/v1/issues/title?mediaType=movie&tmdbId=604", Json))!;
        Assert.False(notInLibrary.InLibrary);
        Assert.False(notInLibrary.CanReport);

        // The film's page lists its open issue; the admin's library filter finds it.
        var page = (await user.GetFromJsonAsync<IssueTitleResponse>("/api/v1/issues/title?mediaType=movie&tmdbId=603", Json))!;
        Assert.Equal([movie.Id], page.Open.Select(i => i.Id));
        var byFilm = (await env.Admin.GetFromJsonAsync<IssuePage>($"/api/v1/issues?status=open&movieId={film.MovieId}", Json))!;
        Assert.Equal([movie.Id], byFilm.Records.Select(i => i.Id));
    }

    [Fact]
    public async Task Users_see_their_own_issues_approvers_see_all_and_only_the_admin_sees_files()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        await using var env = await StartAsync(time: clock);
        await env.FilmInLibraryAsync(withGrab: true);
        var alice = await env.UserAsync("demo-alice", Defaults);
        var bob = await env.UserAsync("demo-bob", Defaults);
        var mute = await env.UserAsync("demo-mute", new UserPermissions { RequestFilms = true });
        var manager = await env.UserAsync("demo-manager", Defaults, UserRole.RequestManager);

        // Without "report issues": refused, and the title page says so.
        Assert.Equal(HttpStatusCode.Forbidden, (await ReportAsync(mute, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "x"))).StatusCode);
        Assert.False((await mute.GetFromJsonAsync<IssueTitleResponse>("/api/v1/issues/title?mediaType=movie&tmdbId=603", Json))!.CanReport);

        var issue = await ReportOkAsync(alice, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Audio, IssueScope.Movie, "Audio out of sync"));

        // Bob neither sees nor touches Alice's issue; his list and the title page are empty.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/issues/{issue.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/comments", new IssueMessageRequest("me too"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsync($"/api/v1/issues/{issue.Id}/resolve", null)).StatusCode);
        Assert.Equal(0, (await bob.GetFromJsonAsync<IssuePage>("/api/v1/issues", Json))!.TotalRecords);
        Assert.Empty((await bob.GetFromJsonAsync<IssueTitleResponse>("/api/v1/issues/title?mediaType=movie&tmdbId=603", Json))!.Open);

        // Users never run actions nor read the approvers' count.
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/v1/issues/{issue.Id}/search", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.GetAsync("/api/v1/issues/count")).StatusCode);

        // The manager sees it, counts it, may act, but sees no file, release or indexer either.
        Assert.Equal(1, (await manager.GetFromJsonAsync<IssueCountsResponse>("/api/v1/issues/count", Json))!.Open);
        Assert.Equal(1, (await manager.GetFromJsonAsync<IssuePage>("/api/v1/issues", Json))!.TotalRecords);
        var managerView = await GetIssueAsync(manager, issue.Id);
        Assert.Null(managerView.File);
        Assert.NotNull(managerView.Actions);

        // No leak: the raw payloads the reporter and the manager get never carry the path, the release or the indexer.
        foreach (var (client, path) in new[] { (alice, $"/api/v1/issues/{issue.Id}"), (alice, "/api/v1/issues"), (alice, "/api/v1/issues/title?mediaType=movie&tmdbId=603"), (manager, $"/api/v1/issues/{issue.Id}") })
        {
            var raw = await client.GetStringAsync(path);
            Assert.DoesNotContain("Bluray-1080p.mkv", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("BluRay.x264-DEMO", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("Demo indexer", raw, StringComparison.Ordinal);
        }

        var adminRaw = await env.Admin.GetStringAsync($"/api/v1/issues/{issue.Id}");
        Assert.Contains("BluRay.x264-DEMO", adminRaw, StringComparison.Ordinal);

        // Comments: the author edits theirs within the window, not after; the admin always may; the manager may not change Alice's.
        var commented = await CommentOkAsync(alice, issue.Id, "From minute 12");
        var comment = commented.Comments!.Single(c => c.Kind == IssueCommentKind.Comment);
        Assert.True(comment.CanEdit);
        var edited = await alice.PutAsJsonAsync($"/api/v1/issues/{issue.Id}/comments/{comment.Id}", new IssueMessageRequest("From minute 12, both tracks"), Json);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.NotNull((await edited.Content.ReadFromJsonAsync<IssueResponse>(Json))!.Comments!.Single(c => c.Id == comment.Id).EditedAt);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/v1/issues/{issue.Id}/comments/{comment.Id}")).StatusCode);
        clock.Advance(IssueService.CommentEditWindow + TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync($"/api/v1/issues/{issue.Id}/comments/{comment.Id}", new IssueMessageRequest("late"), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.DeleteAsync($"/api/v1/issues/{issue.Id}/comments/{comment.Id}")).StatusCode);

        // The manager resolves it (an approver), and lists by status and type.
        await PostOkAsync(manager, $"/api/v1/issues/{issue.Id}/resolve");
        Assert.Equal(0, (await manager.GetFromJsonAsync<IssuePage>("/api/v1/issues?status=open", Json))!.TotalRecords);
        Assert.Equal(1, (await manager.GetFromJsonAsync<IssuePage>("/api/v1/issues?status=resolved&type=audio&q=matrix", Json))!.TotalRecords);
        Assert.Equal(0, (await manager.GetFromJsonAsync<IssuePage>("/api/v1/issues?type=video", Json))!.TotalRecords);
    }

    [Fact]
    public async Task Blocklist_and_delete_act_on_the_file_through_the_library()
    {
        await using var env = await StartAsync();
        var film = await env.FilmInLibraryAsync(withGrab: true);
        var user = await env.UserAsync("demo-friend", Defaults);
        var issue = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks"));

        // Blocklist the release that produced the file, without deleting it: the grab's release is blocklisted for the version.
        var blocked = await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/blocklist", new IssueBlocklistRequest(), Json);
        Assert.True(blocked.IsSuccessStatusCode, await blocked.Content.ReadAsStringAsync());
        var entries = await env.DbAsync(db => db.Blocklist.Where(b => b.TargetId == film.VersionId).Select(b => new { b.SourceTitle, b.Reason }).ToListAsync());
        Assert.Equal(Release, Assert.Single(entries).SourceTitle);
        Assert.Contains($"issue #{issue.Id}", entries[0].Reason, StringComparison.Ordinal);
        Assert.True(await env.DbAsync(db => db.MediaFiles.AnyAsync(f => f.VersionId == film.VersionId)));

        // Delete the file (without searching): gone from the library, the action in the thread; nothing left to delete.
        var deleted = await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/delete-file", new IssueDeleteFileRequest(Search: false), Json);
        Assert.True(deleted.IsSuccessStatusCode, await deleted.Content.ReadAsStringAsync());
        var after = (await deleted.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
        Assert.False(await env.DbAsync(db => db.MediaFiles.AnyAsync(f => f.VersionId == film.VersionId)));
        Assert.Equal([IssueAction.BlocklistAndSearch, IssueAction.DeleteFile], after.Comments!.Where(c => c.Kind == IssueCommentKind.Action).Select(c => c.Action!.Value));
        Assert.Contains(FilmPath, after.Comments!.Last().Detail, StringComparison.Ordinal);
        Assert.True(after.FileChanged);
        Assert.Null(after.CurrentFile);
        Assert.False(after.Actions!.DeleteFile);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/delete-file", new IssueDeleteFileRequest(), Json)).StatusCode);

        // The reporter sees that something was done, not what.
        var mine = await GetIssueAsync(user, issue.Id);
        Assert.All(mine.Comments!, c => Assert.Null(c.Detail));
        Assert.Contains("deleted", mine.Comments!.Last().Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_shadow_mode_the_file_actions_are_off_and_the_search_only_records_a_pick()
    {
        await using var env = await StartAsync(shadow: true);
        var film = await env.FilmInLibraryAsync(withGrab: true);
        var user = await env.UserAsync("demo-friend", Defaults);
        var issue = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Video, IssueScope.Movie, "Green blocks"));

        var view = await GetIssueAsync(env.Admin, issue.Id);
        Assert.Equal(IssueActionService.Shadow, view.Actions!.ManagedBy);
        Assert.True(view.Actions.Search);
        Assert.False(view.Actions.Blocklist);
        Assert.False(view.Actions.DeleteFile);

        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/blocklist", new IssueBlocklistRequest(DeleteFile: true), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/delete-file", new IssueDeleteFileRequest(), Json)).StatusCode);
        Assert.True(await env.DbAsync(db => db.MediaFiles.AnyAsync(f => f.VersionId == film.VersionId)));
        Assert.False(await env.DbAsync(db => db.Blocklist.AnyAsync()));

        var searched = await PostOkAsync(env.Admin, $"/api/v1/issues/{issue.Id}/search");
        Assert.Equal(IssueAction.Search, searched.Comments!.Single(c => c.Kind == IssueCommentKind.Action).Action);
    }

    [Fact]
    public async Task A_followed_Radarr_owns_its_versions_files()
    {
        await using var env = await StartAsync();
        var film = await env.FilmInLibraryAsync(withGrab: true);
        await env.DbAsync(db =>
        {
            db.FollowedRadarrs.Add(new FollowedRadarr { Name = "Demo Radarr", Url = "http://radarr.invalid", ApiKey = "demo-key", VersionName = film.VersionName });
            return db.SaveChangesAsync();
        });
        var user = await env.UserAsync("demo-friend", Defaults);
        var issue = await ReportOkAsync(user, new CreateIssueRequest(RequestMediaType.Movie, 603, IssueType.Audio, IssueScope.Movie, "No French track"));

        var view = await GetIssueAsync(env.Admin, issue.Id);
        Assert.Equal("Demo Radarr", view.Actions!.ManagedBy);
        Assert.False(view.Actions.DeleteFile);
        var refused = await env.Admin.PostAsJsonAsync($"/api/v1/issues/{issue.Id}/delete-file", new IssueDeleteFileRequest(), Json);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Demo Radarr", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(await env.DbAsync(db => db.MediaFiles.AnyAsync(f => f.VersionId == film.VersionId)));
    }

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, CreateIssueRequest body) => client.PostAsJsonAsync("/api/v1/issues", body, Json);

    private static async Task<IssueResponse> ReportOkAsync(HttpClient client, CreateIssueRequest body)
    {
        var response = await ReportAsync(client, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
    }

    private static async Task<IssueResponse> GetIssueAsync(HttpClient client, int id) => (await client.GetFromJsonAsync<IssueResponse>($"/api/v1/issues/{id}", Json))!;

    private static async Task<IssueResponse> CommentOkAsync(HttpClient client, int id, string message)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/issues/{id}/comments", new IssueMessageRequest(message), Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
    }

    private static async Task<IssueResponse> PostOkAsync(HttpClient client, string path)
    {
        var response = await client.PostAsync(path, null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IssueResponse>(Json))!;
    }
}

internal sealed record FilmInLibrary(int MovieId, int VersionId, string VersionName);

internal static class IssuesEnv
{
    /// <summary>The Matrix (603) added by the admin, with a file and, optionally, the grab and import line that produced it.</summary>
    public static async Task<FilmInLibrary> FilmInLibraryAsync(this RequestsEnv env, bool withGrab)
    {
        await AskOkAsync(env.Admin, RequestMediaType.Movie, 603);
        return await env.DbAsync(async db =>
        {
            var version = await db.MovieVersions.Include(v => v.Movie).SingleAsync(v => v.Movie.TmdbId == 603);
            var file = new MediaFile
            {
                Version = version,
                RelativePath = "The Matrix (1999) Bluray-1080p.mkv",
                Size = 8_000_000_000,
                QualityId = TestDataIds.Bluray,
                OriginalReleaseTitle = withGrab ? "The.Matrix.1999.1080p.BluRay.x264-DEMO" : null,
                ReleaseGroup = "DEMO",
            };
            db.MediaFiles.Add(file);
            if (withGrab)
            {
                var grab = new Grab { ReleaseTitle = "The.Matrix.1999.1080p.BluRay.x264-DEMO", ReleaseGuid = "demo-guid-603", DownloadId = "demo-hash-603", IndexerName = "Demo indexer", Size = 8_000_000_000 };
                grab.AddTarget(version);
                db.Grabs.Add(grab);
                db.History.Add(new HistoryEvent
                {
                    TargetId = version.Id,
                    MovieId = version.MovieId,
                    TmdbId = 603,
                    MovieTitle = version.Movie.Title,
                    VersionName = version.Name,
                    EventType = HistoryEventType.Imported,
                    SourceTitle = grab.ReleaseTitle,
                    IndexerName = grab.IndexerName,
                    MediaFile = file,
                    Data = new Dictionary<string, string> { ["downloadId"] = "demo-hash-603" },
                });
            }

            await db.SaveChangesAsync();
            return new FilmInLibrary(version.MovieId, version.Id, version.Name);
        });
    }

    /// <summary>Breaking Bad (1396) added by the admin, season 1 asked: its version's id and name.</summary>
    public static async Task<(int VersionId, string VersionName)> SeriesInLibraryAsync(this RequestsEnv env)
    {
        await AskOkAsync(env.Admin, RequestMediaType.Series, 1396, seasons: [1]);
        return await env.DbAsync(async db =>
        {
            var version = await db.SeriesVersions.SingleAsync(v => v.Series.TmdbId == 1396);
            return (version.Id, version.Name);
        });
    }
}
