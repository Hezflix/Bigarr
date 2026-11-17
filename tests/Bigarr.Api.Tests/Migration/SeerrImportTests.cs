using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Tests.Issues;
using Bigarr.Api.Tests.Requests;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Discover;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Moving in from Seerr: an invented Seerr database (owner, a user Bigarr knows by Plex, a new Plex user, a local user;
/// films and a series in and out of the library; requests in every status; issues on a film, an episode, a season and a
/// title the library lacks; Discover rows) is previewed, imported without a single notification, forward or search,
/// then imported again without adding anything. Every name, id and address here is made up.
/// </summary>
public sealed class SeerrImportTests
{
    private const string FriendDiscord = "123456789012345678";

    [Fact]
    public async Task A_seerr_history_comes_in_attached_to_its_titles_and_users_once_and_silently()
    {
        await using var env = await StartAsync();
        await env.FilmInLibraryAsync(withGrab: false);
        await env.SeriesInLibraryAsync();
        await env.UserAsync("demo-plex", UserPermissions.Defaults());
        await env.DbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Username == "demo-plex");
            db.ExternalLogins.Add(new ExternalLogin { Provider = "plex", ExternalId = "5001", UserId = user.Id });
            return await db.SaveChangesAsync();
        });

        var path = Path.Combine(Directory.CreateTempSubdirectory("bigarr-seerr-").FullName, "db.sqlite3");
        InventedSeerr.Create(path);

        // The admin's own Matrix request is available already: its message goes out before the import, not because of it.
        await env.RunJobAsync(RequestAvailabilityJob.JobName);
        var outboxBefore = await env.OutboxTypesAsync();
        var requestsBefore = await env.DbAsync(db => db.MediaRequests.CountAsync());

        // ---- preview: nothing written ----
        var previewResponse = await env.Admin.PostAsJsonAsync("/api/v1/migration/seerr/preview", new SeerrImportPreviewRequest(path), Json);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<SeerrImportPreviewResponse>(Json))!;
        Assert.Equal("Seerr", preview.Flavor);
        Assert.Equal(
            [(1, SeerrUserMatch.Owner, "admin"), (2, SeerrUserMatch.PlexId, "demo-plex"), (3, SeerrUserMatch.NewPlex, "demo-friend"), (4, SeerrUserMatch.NewWithoutSignIn, "demo-local")],
            preview.Users.Select(u => (u.SeerrId, u.Match, u.BigarrUsername)));
        var friend = preview.Users.Single(u => u.SeerrId == 3);
        Assert.Equal(UserRole.User, friend.Role);
        Assert.True(friend.Permissions is { RequestFilms: true, RequestSeries: true, Request4K: true, AutoApproveFilms: true, AutoApproveSeries: false, ReportIssues: true });
        Assert.Equal(["Vote"], friend.Skipped);
        Assert.True(friend.DiscordId);
        Assert.Equal(UserRole.RequestManager, preview.Users.Single(u => u.SeerrId == 4).Role);

        Assert.Equal(new SeerrRequestCounts(6, 5, 1, 1, 2, 1, 1, 1, 1, 2, 3, 0, 1, 1), preview.Requests);
        Assert.Equal(new SeerrIssueCounts(4, 3, 1, 5, 3, 1, 0, 0, 0), preview.Issues);
        Assert.Equal([(604, 3), (999999, 1)], preview.TitlesNotInLibrary.Select(t => (t.TmdbId, t.Requests + t.Issues)));
        Assert.Equal(2, preview.TitlesToAskTmdb);
        var skipped = Assert.Single(preview.Skipped);
        Assert.Equal(("request", 6, "Its title is gone from Seerr."), (skipped.Kind, skipped.SeerrId, skipped.Reason));
        Assert.Contains(preview.NotImported, n => n.What == "File at report time" && n.Count == 4);
        Assert.Contains(preview.Sliders, s => s.Name == "Recently added" && s.Kind is null && s.Skipped is not null);
        Assert.Contains(preview.Sliders, s => s.Name == "Studio" && s.Kind == "studio" && s.Value == "420");
        Assert.Contains(preview.Sliders, s => s.Name == "Movie keywords" && s.Kind is null && s.Skipped!.Contains("180547,1234", StringComparison.Ordinal));
        Assert.Equal(requestsBefore, await env.DbAsync(db => db.MediaRequests.CountAsync()));
        Assert.False(await env.DbAsync(db => db.Users.AnyAsync(u => u.Username == "demo-friend")));

        // ---- import ----
        var report = await ImportAsync(env.Admin, path);
        Assert.Equal(2, report.UsersAdded);
        Assert.Equal(2, report.UsersMatched);
        Assert.Equal(5, report.RequestsAdded);
        Assert.Equal(4, report.IssuesAdded);
        Assert.Equal(1, report.CommentsAdded);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(1, report.TitlesFromTmdb);
        Assert.Equal(2, report.SlidersAdded);
        Assert.Contains(report.Issues, i => i.Kind == "user" && i.What == "demo-local");
        Assert.Contains(report.Issues, i => i.Kind == "request" && i.What == "TMDB 999999");

        // Nothing announced, forwarded or searched: the outbox is as it was, and stays so after the availability job.
        Assert.Equal(outboxBefore, await env.OutboxTypesAsync());
        await env.RunJobAsync(RequestAvailabilityJob.JobName);
        Assert.Equal(outboxBefore, await env.OutboxTypesAsync());

        await env.DbAsync(async db =>
        {
            var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Username);
            var logins = await db.ExternalLogins.AsNoTracking().ToListAsync();

            // A new Plex user signs in with Plex later; a local one has no way in yet. Nothing secret came along.
            var demoFriend = users["demo-friend"];
            Assert.Equal("seerr:user:3", demoFriend.SourceRef);
            Assert.Equal(string.Empty, demoFriend.PasswordHash);
            Assert.Equal(5, demoFriend.Quota.FilmLimit);
            Assert.Equal(14, demoFriend.Quota.FilmDays);
            Assert.Null(demoFriend.Quota.SeasonLimit);
            Assert.Equal(FriendDiscord, demoFriend.DiscordUserId);
            Assert.Equal(["requestApproved", "requestAvailable", "issueCommented"], demoFriend.DiscordMentions);
            Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), demoFriend.CreatedAt);
            var friendLogin = logins.Single(l => l.UserId == demoFriend.Id);
            Assert.Equal(("plex", "5002", "demo-friend"), (friendLogin.Provider, friendLogin.ExternalId, friendLogin.ExternalUsername));
            Assert.DoesNotContain(logins, l => l.UserId == users["demo-local"].Id);
            Assert.Equal(UserRole.RequestManager, users["demo-local"].Role);

            // The matched ones keep what they had.
            Assert.Equal(UserRole.Admin, users["admin"].Role);
            Assert.Null(users["admin"].SourceRef);
            Assert.Null(users["demo-plex"].SourceRef);

            var requests = await db.MediaRequests.AsNoTracking().Include(r => r.RequestedBy).Include(r => r.DecidedBy)
                .Where(r => r.SourceRef != null).ToDictionaryAsync(r => r.SourceRef!);
            Assert.Equal(5, requests.Count);

            // Completed in Seerr: approved, linked to the library's film, already available so no message later.
            var completed = requests["seerr:request:1"];
            Assert.Equal((RequestStatus.Approved, "demo-friend", true), (completed.Status, completed.RequestedBy.Username, completed.AutoApproved));
            Assert.NotNull(completed.MovieId);
            Assert.NotNull(completed.AvailableAt);
            Assert.Equal("The Matrix", completed.Title);
            Assert.Equal(ForwardStatus.None, completed.ForwardStatus);

            var pending = requests["seerr:request:2"];
            Assert.Equal((RequestStatus.Pending, "The Matrix Reloaded", 2003), (pending.Status, pending.Title, pending.Year));
            Assert.Null(pending.MovieId);
            Assert.Null(pending.DecidedById);
            Assert.Equal(new DateTime(2024, 2, 1, 10, 0, 0, DateTimeKind.Utc), pending.CreatedAt);

            var series = requests["seerr:request:3"];
            Assert.Equal((RequestStatus.Approved, "demo-plex", "admin", false), (series.Status, series.RequestedBy.Username, series.DecidedBy!.Username, series.AutoApproved));
            Assert.Equal([1, 2], series.Seasons);
            Assert.NotNull(series.SeriesId);
            Assert.Equal(81189, series.TvdbId);
            Assert.Null(series.AvailableAt);

            var declined = requests["seerr:request:4"];
            Assert.Equal((RequestStatus.Declined, true, "demo-local"), (declined.Status, declined.FourK, declined.DecidedBy!.Username));

            var failed = requests["seerr:request:5"];
            Assert.Equal((RequestStatus.Failed, "TMDB 999999"), (failed.Status, failed.Title));
            Assert.NotNull(failed.Error);

            var issues = await db.Issues.AsNoTracking().Include(i => i.Comments).Include(i => i.ReportedBy).Where(i => i.SourceRef != null).ToDictionaryAsync(i => i.SourceRef!);
            Assert.Equal(4, issues.Count);

            var film = issues["seerr:issue:1"];
            Assert.Equal((IssueType.Audio, IssueScope.Movie, IssueStatus.Open, "demo-friend"), (film.Type, film.Scope, film.Status, film.ReportedBy.Username));
            Assert.Equal("No sound after ten minutes", film.Message);
            Assert.NotNull(film.MovieId);
            Assert.NotNull(film.MovieVersionId);
            Assert.Null(film.File);
            var reply = Assert.Single(film.Comments);
            Assert.Equal(("Looking into it.", "seerr:comment:2"), (reply.Message, reply.SourceRef));

            var episode = issues["seerr:issue:2"];
            Assert.Equal((IssueScope.Episode, IssueStatus.Resolved, 1), (episode.Scope, episode.Status, episode.SeasonNumber));
            Assert.NotNull(episode.EpisodeId);
            Assert.StartsWith("S01E02 · ", episode.EpisodeLabel, StringComparison.Ordinal);
            Assert.Equal(users["admin"].Id, episode.ResolvedById);
            Assert.Equal(IssueCommentKind.Resolved, Assert.Single(episode.Comments).Kind);

            var season = issues["seerr:issue:3"];
            Assert.Equal((IssueScope.Season, 2, (int?)null), (season.Scope, season.SeasonNumber, season.EpisodeId));

            var outside = issues["seerr:issue:4"];
            Assert.Equal((IssueType.Other, "The Matrix Reloaded", (int?)null, "default"), (outside.Type, outside.Title, outside.MovieId, outside.VersionName));

            // Seerr's Discover order and on/off, the custom rows Bigarr can show added.
            var sliders = await db.DiscoverSliders.AsNoTracking().OrderBy(s => s.Position).ToListAsync();
            Assert.Equal(
                [DiscoverSliderKind.Trending, DiscoverSliderKind.PopularMovies, DiscoverSliderKind.Studio, DiscoverSliderKind.SeriesGenre],
                sliders.Take(4).Select(s => s.Kind));
            Assert.False(sliders[1].Enabled);
            Assert.Equal(("Demo studio", "420", true), (sliders[2].Title, sliders[2].Value, sliders[2].Enabled));
            Assert.Equal(("Demo dramas", "18", false), (sliders[3].Title, sliders[3].Value, sliders[3].Enabled));
            return 0;
        });

        // ---- again: nothing new ----
        var again = await ImportAsync(env.Admin, path);
        Assert.Equal((0, 0, 0, 0, 0), (again.UsersAdded, again.RequestsAdded, again.IssuesAdded, again.CommentsAdded, again.SlidersAdded));
        Assert.Equal(2 + 5 + 4, again.AlreadyImported); // the two users it created, the requests, the issues
        Assert.Equal(5, await env.DbAsync(db => db.MediaRequests.CountAsync(r => r.SourceRef != null)));
        Assert.Equal(outboxBefore, await env.OutboxTypesAsync());

        var previewAgain = (await (await env.Admin.PostAsJsonAsync("/api/v1/migration/seerr/preview", new SeerrImportPreviewRequest(path), Json)).Content.ReadFromJsonAsync<SeerrImportPreviewResponse>(Json))!;
        Assert.Equal(5, previewAgain.Requests.AlreadyImported);
        Assert.Equal(4, previewAgain.Issues.AlreadyImported);
        Assert.All(previewAgain.Users, u => Assert.NotEqual(SeerrUserMatch.NewPlex, u.Match));
    }

    [Fact]
    public async Task An_overseerr_database_reads_its_single_discord_id_and_a_bad_file_is_refused()
    {
        await using var env = await StartAsync();
        var path = Path.Combine(Directory.CreateTempSubdirectory("bigarr-overseerr-").FullName, "db.sqlite3");
        using (var connection = Open(path))
        {
            Exec(connection, SeerrSchema.Overseerr);
            Exec(connection, """
                INSERT INTO "user" (id, email, plexUsername, plexId, permissions, avatar, userType, createdAt) VALUES (7, 'old@demo.invalid', 'demo-old', 6001, 32, '', 1, '2021-05-06 07:08:09');
                INSERT INTO user_settings (userId, discordId, notificationTypes) VALUES (7, '223456789012345678', '{"discord":2}');
                """);
        }

        var preview = (await (await env.Admin.PostAsJsonAsync("/api/v1/migration/seerr/preview", new SeerrImportPreviewRequest(path), Json)).Content.ReadFromJsonAsync<SeerrImportPreviewResponse>(Json))!;
        Assert.Equal("Overseerr", preview.Flavor);
        var user = Assert.Single(preview.Users);
        Assert.Equal((SeerrUserMatch.NewPlex, "demo-old", true), (user.Match, user.BigarrUsername, user.DiscordId));
        Assert.True(user.Permissions is { RequestFilms: true, RequestSeries: true, ReportIssues: false });

        var report = await ImportAsync(env.Admin, path);
        Assert.Equal(1, report.UsersAdded);
        var created = await env.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Username == "demo-old"));
        Assert.Equal("223456789012345678", created.DiscordUserId);
        Assert.Equal(["requestCreated"], created.DiscordMentions);

        var notSeerr = Path.Combine(Path.GetDirectoryName(path)!, "other.db");
        using (var connection = Open(notSeerr))
        {
            Exec(connection, "CREATE TABLE things (id integer)");
        }

        var refused = await env.Admin.PostAsJsonAsync("/api/v1/migration/seerr/preview", new SeerrImportPreviewRequest(notSeerr), Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("no user table", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, UserRole.Admin, true, true, true, true, true, true)]
    [InlineData(32 | 4194304, UserRole.User, true, true, false, false, false, true)]
    [InlineData(262144 | 4096 | 512, UserRole.User, true, false, true, false, true, false)]
    [InlineData(16 | 128, UserRole.RequestManager, false, false, false, true, true, false)]
    [InlineData(1048576, UserRole.RequestManager, false, false, false, false, false, false)]
    public void Seerr_permission_bits_map_to_a_role_and_switches(long bits, UserRole role, bool films, bool series, bool fourK, bool autoFilms, bool autoSeries, bool issues)
    {
        var access = SeerrMapping.Access(bits);
        Assert.Equal(role, access.Role);
        Assert.Equal(
            (films, series, fourK, autoFilms, autoSeries, issues),
            (access.Permissions.RequestFilms, access.Permissions.RequestSeries, access.Permissions.Request4K, access.Permissions.AutoApproveFilms, access.Permissions.AutoApproveSeries, access.Permissions.ReportIssues));
    }

    [Fact]
    public void Seerr_bits_bigarr_has_no_switch_for_are_named()
    {
        Assert.Equal(["Manage users", "Auto-approve 4K", "Auto-request (watchlist)"], SeerrMapping.Access(8 | 32768 | 8388608).Skipped);
        Assert.Empty(SeerrMapping.Access(2 | 8).Skipped);
    }

    private static async Task<SeerrImportStatusResponse> ImportAsync(HttpClient admin, string path)
    {
        var started = await admin.PostAsJsonAsync("/api/v1/migration/seerr/import", new SeerrImportRequest(path), Json);
        Assert.True(started.StatusCode == HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = (await admin.GetFromJsonAsync<SeerrImportStatusResponse>("/api/v1/migration/seerr/status", Json))!;
            if (status.State == RadarrImportState.Completed)
            {
                return status;
            }

            Assert.True(status.State != RadarrImportState.Failed, status.Error);
            await Task.Delay(50);
        }

        throw new TimeoutException("The Seerr import did not finish.");
    }

    internal static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    internal static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

/// <summary>An invented Seerr database, on Seerr's real schema.</summary>
internal static class InventedSeerr
{
    public static void Create(string path)
    {
        using var connection = SeerrImportTests.Open(path);
        SeerrImportTests.Exec(connection, SeerrSchema.Seerr);
        SeerrImportTests.Exec(connection, """
            -- 1 the owner (admin), 2 known to Bigarr by Plex, 3 a new Plex user, 4 a local user who manages requests.
            INSERT INTO "user" (id, email, username, plexUsername, plexId, plexToken, password, permissions, avatar, userType, createdAt, movieQuotaLimit, movieQuotaDays)
            VALUES (1, 'owner@demo.invalid', NULL, 'demo-owner', 1000, 'secret-token-1', NULL, 2, '', 1, '2023-12-01 00:00:00', NULL, NULL),
                   (2, 'plex@demo.invalid', NULL, 'demo-plex', 5001, 'secret-token-2', NULL, 32 | 4194304, '', 1, '2023-12-02 00:00:00', NULL, NULL),
                   (3, 'friend@demo.invalid', NULL, 'demo-friend', 5002, 'secret-token-3', NULL, 262144 | 524288 | 2048 | 256 | 4194304 | 64, '', 1, '2024-01-02 03:04:05.000', 5, 14),
                   (4, 'local@demo.invalid', 'demo-local', NULL, NULL, NULL, 'not-a-real-hash', 16 | 32, '', 2, '2024-01-03 00:00:00', NULL, NULL);
            INSERT INTO user_settings (userId, locale, discordIds, notificationTypes)
            VALUES (3, 'fr', '["123456789012345678"]', '{"discord":524,"email":0}'),
                   (4, '', NULL, NULL);

            -- The Matrix (in the library), its sequel and an unknown film (not), Breaking Bad (in the library).
            INSERT INTO media (id, mediaType, tmdbId, tvdbId, status, status4k) VALUES
                (1, 'movie', 603, NULL, 5, 1),
                (2, 'movie', 604, NULL, 2, 1),
                (3, 'tv', 1396, 81189, 3, 1),
                (4, 'movie', 999999, NULL, 1, 1);

            INSERT INTO media_request (id, status, type, mediaId, requestedById, modifiedById, is4k, createdAt, updatedAt) VALUES
                (1, 5, 'movie', 1, 3, 3, 0, '2024-01-10 09:00:00', '2024-01-12 09:00:00'),
                (2, 1, 'movie', 2, 3, NULL, 0, '2024-02-01 10:00:00', '2024-02-01 10:00:00'),
                (3, 2, 'tv', 3, 2, 1, 0, '2024-02-02 10:00:00', '2024-02-03 10:00:00'),
                (4, 3, 'movie', 2, 3, 4, 1, '2024-02-04 10:00:00', '2024-02-05 10:00:00'),
                (5, 4, 'movie', 4, 2, 1, 0, '2024-02-06 10:00:00', '2024-02-07 10:00:00'),
                (6, 1, 'movie', NULL, 2, NULL, 0, '2024-02-08 10:00:00', '2024-02-08 10:00:00');
            INSERT INTO season_request (requestId, seasonNumber, status) VALUES (3, 2, 2), (3, 1, 2);

            INSERT INTO issue (id, issueType, status, problemSeason, problemEpisode, mediaId, createdById, modifiedById, createdAt, updatedAt) VALUES
                (1, 2, 1, 0, 0, 1, 3, NULL, '2024-03-01 10:00:00', '2024-03-01 11:00:00'),
                (2, 1, 2, 1, 2, 3, 2, 1, '2024-03-02 10:00:00', '2024-03-03 10:00:00'),
                (3, 3, 1, 2, 0, 3, 3, NULL, '2024-03-04 10:00:00', '2024-03-04 10:00:00'),
                (4, 4, 1, 0, 0, 2, 3, NULL, '2024-03-05 10:00:00', '2024-03-05 10:00:00');
            INSERT INTO issue_comment (id, issueId, userId, message, createdAt) VALUES
                (1, 1, 3, 'No sound after ten minutes', '2024-03-01 10:00:00'),
                (2, 1, 1, 'Looking into it.', '2024-03-01 11:00:00'),
                (3, 2, 2, 'Green blocks all over', '2024-03-02 10:00:00'),
                (4, 3, 3, 'Subtitles out of sync', '2024-03-04 10:00:00'),
                (5, 4, 3, 'Wrong film', '2024-03-05 10:00:00');

            INSERT INTO discover_slider (type, "order", isBuiltIn, enabled, title, data) VALUES
                (1, 0, 1, 1, NULL, NULL),
                (4, 1, 1, 1, NULL, NULL),
                (5, 2, 1, 0, NULL, NULL),
                (18, 3, 0, 1, 'Demo studio', '420'),
                (16, 4, 0, 0, 'Demo dramas', '18'),
                (13, 5, 0, 1, 'Demo keywords', '180547,1234'),
                (17, 6, 0, 1, 'Demo search', 'heist');
            """);
    }
}
