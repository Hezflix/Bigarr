using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Api.Tests.Playback;
using Bigarr.Domain.Library;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Playback;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Migration;

/// <summary>
/// Moving in from Tautulli: an invented <c>tautulli.db</c> (films, episodes, a play resumed over two sessions, a deleted
/// user, legacy guids, a row without media info, music) is previewed, imported against a fake Plex, imported again
/// without adding anything, then imported once more after Tautulli recorded the end of a play. Every name, id and
/// address here is made up.
/// </summary>
public sealed class TautulliImportTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private const long T0 = 1_700_000_000;

    [Fact]
    public async Task While_an_upload_is_streaming_no_import_starts_and_no_second_upload_replaces_it()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        var runner = factory.Services.GetRequiredService<Bigarr.Api.Features.Migration.TautulliImportRunner>();

        // An upload is streaming its body.
        Assert.True(runner.TryBeginUpload());
        try
        {
            // An import started now would read the previous file while this upload replaces it, then delete the new one.
            Assert.Null(runner.Enqueue(new Bigarr.Api.Features.Migration.TautulliImportRequest("/nowhere/tautulli.db", 1)));
            var second = await session.PutAsync("/api/v1/migration/tautulli/upload", new ByteArrayContent([1, 2, 3]));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
        finally
        {
            runner.EndUpload();
        }

        // Once it is done the file can be replaced again.
        var after = await session.PutAsync("/api/v1/migration/tautulli/upload", new ByteArrayContent([1, 2, 3]));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        File.Delete((await after.ReadJsonAsync()).GetProperty("path").GetString()!);
    }

    [Fact]
    public async Task A_tautulli_history_becomes_plays_once_and_a_rerun_adds_only_what_is_new()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        await using var plex = await FakePlex.StartAsync();
        plex.Items["40"] = new FakePlex.Item("40", "Invented Sequel", 2022, 7_200_000, "7", [], TmdbId: 990104);
        plex.ShowTvdbIds["900"] = 70002;

        var (serverId, films) = await SeedAsync(plex.Url);
        var path = Path.Combine(Directory.CreateTempSubdirectory("bigarr-tautulli-").FullName, "tautulli.db");
        InventedTautulli.Create(path);

        // Preview: counted from Tautulli alone, Plex asked only for its libraries.
        var preview = await (await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path, mediaServerId = serverId })).ReadJsonAsync();
        Assert.Equal("v2.99.0", preview.GetProperty("tautulliVersion").GetString());
        Assert.Equal(4, preview.GetProperty("users").GetInt32());
        Assert.Equal(1, preview.GetProperty("deletedUsers").GetInt32());
        Assert.Equal(1, preview.GetProperty("usersKnown").GetInt32());
        Assert.Equal(12, preview.GetProperty("historyRows").GetInt32());
        Assert.Equal(11, preview.GetProperty("plays").GetInt32());
        Assert.Equal(6, preview.GetProperty("moviePlays").GetInt32());
        Assert.Equal(3, preview.GetProperty("episodePlays").GetInt32());
        Assert.Equal(1, preview.GetProperty("otherPlays").GetInt32());
        Assert.Equal(6, preview.GetProperty("movies").GetInt32());
        Assert.Equal(3, preview.GetProperty("episodes").GetInt32());
        Assert.Equal(4, preview.GetProperty("moviePlaysWithFilm").GetInt32());
        Assert.Equal(4, preview.GetProperty("moviePlaysWithVersion").GetInt32());
        Assert.Equal(2, preview.GetProperty("moviePlaysAskPlex").GetInt32());
        Assert.Equal(0, preview.GetProperty("moviePlaysUnmatched").GetInt32());
        Assert.Equal(0, preview.GetProperty("alreadyImported").GetInt32());
        Assert.Equal(1, preview.GetProperty("overlappingLive").GetInt32());
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(T0), preview.GetProperty("firstPlayAt").GetDateTime());
        Assert.DoesNotContain(plex.Requests, r => r.Path.StartsWith("/library/metadata/", StringComparison.Ordinal));

        var report = await ImportAsync(session, path, serverId);
        Assert.Equal(11, report.GetProperty("total").GetInt32());
        Assert.Equal(11, report.GetProperty("processed").GetInt32());
        Assert.Equal(3, report.GetProperty("usersAdded").GetInt32());
        Assert.Equal(8, report.GetProperty("itemsAdded").GetInt32());
        Assert.Equal(9, report.GetProperty("playsAdded").GetInt32());
        Assert.Equal(10, report.GetProperty("streamsAdded").GetInt32());
        Assert.Equal(1, report.GetProperty("overlappingLive").GetInt32());
        Assert.Equal(1, report.GetProperty("otherSkipped").GetInt32());
        Assert.Equal(6, report.GetProperty("moviePlays").GetInt32());
        Assert.Equal(5, report.GetProperty("moviePlaysWithFilm").GetInt32());
        Assert.Equal(5, report.GetProperty("moviePlaysWithVersion").GetInt32());
        Assert.Equal(4, report.GetProperty("plexAsked").GetInt32());
        Assert.Equal(2, report.GetProperty("plexResolved").GetInt32());
        var issue = Assert.Single(report.GetProperty("issues").EnumerateArray());
        Assert.Equal("Unknown Invention", issue.GetProperty("title").GetString());
        Assert.Equal(1, issue.GetProperty("plays").GetInt32());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var users = await db.MediaServerUsers.AsNoTracking().Where(u => u.MediaServerId == serverId).ToDictionaryAsync(u => u.ExternalId);
            Assert.Equal(["1", "9002", "9003", "9004"], users.Keys.Order(StringComparer.Ordinal));

            // Dora is in Tautulli's accounts and never played: never seen, not seen at the import.
            Assert.Null(users["9004"].LastSeenAt);
            Assert.NotNull(users["9002"].LastSeenAt);
            Assert.NotEqual(users["9002"].FirstSeenAt, users["9002"].LastSeenAt);
            Assert.Equal("Owner", users["1"].DisplayName);
            Assert.Equal("Alice", users["9002"].DisplayName);
            Assert.False(users["9003"].KeepHistory);

            // Tautulli's deleted flag is a first idea of who left the server, until plex.tv says.
            Assert.True(users["9002"].OnServer);
            Assert.False(users["9003"].OnServer);
            Assert.Null(users["9003"].OnServerCheckedAt);
            Assert.Equal(DateTime.UnixEpoch.AddSeconds(T0 + 20_000), users["9003"].FirstSeenAt);

            var plays = await db.Plays.AsNoTracking().Include(p => p.Item).Include(p => p.Streams).Where(p => p.Source == PlaySource.Tautulli).ToListAsync();
            Assert.Equal(9, plays.Count);

            // Two sessions, one play: paused time left out, watched from the furthest offset, version from the library.
            var resumed = plays.Single(p => p.Item.RatingKey == "10");
            Assert.Equal(2, resumed.Streams.Count);
            Assert.Equal(2400 + 3240, resumed.PlayedSeconds);
            Assert.Equal(60, resumed.PausedSeconds);
            Assert.Equal(5_700_000, resumed.MaxOffsetMs);
            Assert.True(resumed.Watched);
            Assert.Equal(films.InventedFilm, resumed.MovieId);
            Assert.Equal("1080p", resumed.VersionName);
            Assert.Equal(users["9002"].Id, resumed.UserId);

            // Tautulli keeps no start position: the resumed stream starts where the first one stopped.
            var sessions = resumed.Streams.OrderBy(s => s.StartedAt).ToList();
            Assert.Equal(0, sessions[0].StartOffsetMs);
            Assert.Equal(2_400_000, sessions[1].StartOffsetMs);
            Assert.Equal(DateTime.UnixEpoch.AddSeconds(T0), resumed.StartedAt);

            Assert.Equal("4k", plays.Single(p => p.Item.RatingKey == "60").VersionName);
            Assert.Equal(films.InventedFilm, plays.Single(p => p.Item.RatingKey == "60").MovieId);
            Assert.Equal(films.Picture, plays.Single(p => p.Item.RatingKey == "20").MovieId);
            Assert.False(plays.Single(p => p.Item.RatingKey == "20").Watched);
            Assert.Equal(films.Classic, plays.Single(p => p.Item.RatingKey == "30").MovieId);
            Assert.True(plays.Single(p => p.Item.RatingKey == "30").Watched);
            Assert.Equal(users["9003"].Id, plays.Single(p => p.Item.RatingKey == "30").UserId);
            var sequel = plays.Single(p => p.Item.RatingKey == "40");
            Assert.Equal(films.Sequel, sequel.MovieId);
            Assert.Equal("4k", sequel.VersionName);
            Assert.Null(plays.Single(p => p.Item.RatingKey == "50").MovieId);

            var transcoded = sequel.Streams.Single();
            Assert.Equal("tautulli:5", transcoded.ExternalKey);
            Assert.Equal(StreamDecision.Transcode, transcoded.Details.Decision);
            Assert.Equal("transcode", transcoded.Details.VideoDecision);
            Assert.Equal("198.51.100.20", transcoded.Details.IpAddress);
            Assert.False(transcoded.Details.Local);
            Assert.Equal("HDR10", transcoded.Details.SourceDynamicRange);
            Assert.True(transcoded.Details.HardwareTranscode);

            var bare = plays.Single(p => p.Item.RatingKey == "20").Streams.Single();
            Assert.Null(bare.Details.Decision);
            Assert.Equal("Bedroom TV", bare.Details.PlayerTitle);
            Assert.True(bare.Details.Local);

            var items = await db.MediaServerItems.AsNoTracking().Where(i => i.MediaServerId == serverId).ToDictionaryAsync(i => i.RatingKey);
            Assert.Equal("Un classique inventé", items["30"].OriginalTitle);
            Assert.False(items.ContainsKey("70"));
            Assert.Equal(70001, items["801"].ShowTvdbId);
            Assert.Equal(70001, items["802"].ShowTvdbId);
            Assert.Equal(70002, items["803"].ShowTvdbId);
            Assert.Equal("Invented Show", items["801"].ShowTitle);
            Assert.Equal("Plex show 900", items["803"].ShowTitle);

            // Linked films carry Bigarr's title, unmatched ones Tautulli's.
            Assert.Equal("An Invented Picture", items["20"].Title);
            Assert.Equal("Unknown Invention", items["50"].Title);
            Assert.Equal(1, items["801"].SeasonNumber);
            Assert.Equal(2, items["802"].EpisodeNumber);
            Assert.Equal(MediaItemKind.Episode, items["803"].Kind);
            Assert.Equal(990104, items["40"].TmdbId);
            Assert.Equal(films.Sequel, items["40"].MovieId);
            Assert.Equal(4_800_000, items["20"].CreditsStartMs);

            // The live play wins: the overlapping row was left out.
            Assert.Single(await db.Plays.AsNoTracking().Where(p => p.ItemId == items["40"].Id && p.UserId == users["1"].Id).ToListAsync());
        }

        // The Plays page shows Bigarr's film title, and lists the film plays left unmatched.
        var sequelPlays = await (await session.GetAsync($"/api/v1/playback/plays?movieId={films.Sequel}")).ReadJsonAsync();
        Assert.Equal("Invented Sequel", sequelPlays.GetProperty("records")[0].GetProperty("item").GetProperty("title").GetString());
        var unmatched = await (await session.GetAsync("/api/v1/playback/plays?unmatched=true")).ReadJsonAsync();
        Assert.Equal(1, unmatched.GetProperty("totalRecords").GetInt32());
        Assert.Equal("Unknown Invention", unmatched.GetProperty("records")[0].GetProperty("item").GetProperty("title").GetString());

        // An item imported before Bigarr kept original titles gets Tautulli's on the next run.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            await db.MediaServerItems.Where(i => i.MediaServerId == serverId && i.RatingKey == "30").ExecuteUpdateAsync(u => u.SetProperty(i => i.OriginalTitle, (string?)null));
        }

        // Again: nothing new.
        var again = await ImportAsync(session, path, serverId);
        Assert.Equal(0, again.GetProperty("playsAdded").GetInt32());
        Assert.Equal(0, again.GetProperty("streamsAdded").GetInt32());
        Assert.Equal(0, again.GetProperty("usersAdded").GetInt32());
        Assert.Equal(0, again.GetProperty("itemsAdded").GetInt32());
        Assert.Equal(9, again.GetProperty("alreadyImported").GetInt32());
        Assert.Equal(1, again.GetProperty("overlappingLive").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal("Un classique inventé", (await db.MediaServerItems.AsNoTracking().SingleAsync(i => i.MediaServerId == serverId && i.RatingKey == "30")).OriginalTitle);
        }

        // Tautulli recorded the end of a play since: its row joins the play imported before.
        InventedTautulli.AddRow(path, id: 13, referenceId: 3, started: T0 + 12_000, stopped: T0 + 15_000, ratingKey: 20, userId: 9001, viewOffsetMs: 5_390_000);
        var resumedLater = await ImportAsync(session, path, serverId);
        Assert.Equal(0, resumedLater.GetProperty("playsAdded").GetInt32());
        Assert.Equal(1, resumedLater.GetProperty("playsExtended").GetInt32());
        Assert.Equal(1, resumedLater.GetProperty("streamsAdded").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var picture = await db.Plays.AsNoTracking().Include(p => p.Streams).SingleAsync(p => p.Item.RatingKey == "20" && p.Item.MediaServerId == serverId);
            Assert.Equal(2, picture.Streams.Count);
            Assert.True(picture.Watched);
            Assert.Equal(DateTime.UnixEpoch.AddSeconds(T0 + 15_000), picture.StoppedAt);
        }

        // An uploaded copy previews like a typed path and is deleted once imported.
        var upload = await session.PutAsync("/api/v1/migration/tautulli/upload", new ByteArrayContent(await File.ReadAllBytesAsync(path)));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = (await upload.ReadJsonAsync()).GetProperty("path").GetString()!;
        Assert.True(File.Exists(uploaded));
        var uploadedPreview = await (await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path = uploaded, mediaServerId = serverId })).ReadJsonAsync();
        Assert.Equal(9, uploadedPreview.GetProperty("alreadyImported").GetInt32());
        var fromUpload = await ImportAsync(session, uploaded, serverId);
        Assert.Equal(0, fromUpload.GetProperty("playsAdded").GetInt32());
        Assert.True(fromUpload.GetProperty("uploadDeleted").GetBoolean());
        Assert.False(File.Exists(uploaded));
    }

    [Fact]
    public async Task An_older_tautulli_database_reads_with_what_it_has_and_other_files_are_refused()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        int serverId;
        int jellyfinId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var server = db.MediaServers.Add(new MediaServer { Name = "Old Plex", Kind = MediaServerKind.Plex, Url = "http://127.0.0.1:9", Token = "t", Enabled = false }).Entity;
            var jellyfin = db.MediaServers.Add(new MediaServer { Name = "Jellyfin", Kind = MediaServerKind.Jellyfin, Url = "http://127.0.0.1:9", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            serverId = server.Id;
            jellyfinId = jellyfin.Id;
        }

        var folder = Directory.CreateTempSubdirectory("bigarr-tautulli-old-").FullName;
        var old = Path.Combine(folder, "old.db");
        InventedTautulli.CreateOld(old);
        var preview = await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path = old, mediaServerId = serverId });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var counts = await preview.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, counts.GetProperty("tautulliVersion").ValueKind);
        Assert.Equal(2, counts.GetProperty("plays").GetInt32());
        Assert.Equal(1, counts.GetProperty("moviePlays").GetInt32());
        Assert.Equal(1, counts.GetProperty("episodePlays").GetInt32());
        Assert.Equal(1, counts.GetProperty("moviePlaysUnmatched").GetInt32());

        var notTautulli = Path.Combine(folder, "other.db");
        using (var connection = new SqliteConnection($"Data Source={notTautulli};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE things (id INTEGER)";
            command.ExecuteNonQuery();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path = notTautulli, mediaServerId = serverId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path = Path.Combine(folder, "missing.db"), mediaServerId = serverId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await session.PostAsJsonAsync("/api/v1/migration/tautulli/preview", new { path = old, mediaServerId = jellyfinId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.PostAsJsonAsync("/api/v1/migration/tautulli/import", new { path = old, mediaServerId = 999_999 })).StatusCode);
    }

    private static async Task<JsonElement> ImportAsync(HttpClient session, string path, int serverId)
    {
        var started = await session.PostAsJsonAsync("/api/v1/migration/tautulli/import", new { path, mediaServerId = serverId });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = await (await session.GetAsync("/api/v1/migration/tautulli/status")).ReadJsonAsync();
            var state = status.GetProperty("state").GetString();
            if (state == "completed")
            {
                return status;
            }

            Assert.NotEqual("failed", state);
            await Task.Delay(100);
        }

        throw new TimeoutException("The Tautulli import did not finish.");
    }

    private sealed record Films(int InventedFilm, int Picture, int Classic, int Sequel);

    /// <summary>
    /// Bigarr's side: four films (two with a 4k version under the 4K root), a Plex server whose <c>/media</c> is
    /// Bigarr's <c>/data</c>, and the owner already followed live, watching the sequel while Tautulli also recorded it.
    /// </summary>
    private async Task<(int ServerId, Films Films)> SeedAsync(string plexUrl)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        // The HD root first: the seeded films take the first root folder.
        db.RootFolders.Add(new RootFolder { Path = "/data/movies" });
        await db.SaveChangesAsync();
        var uhdRoot = db.RootFolders.Add(new RootFolder { Path = "/data/movies4k" }).Entity;
        await db.SaveChangesAsync();

        var (invented, inventedHd) = await TestData.SeedMovieAsync(db, tmdbId: 990101);
        Rename(invented, "Invented Film", 2020);
        db.MediaFiles.Add(TestData.NewFile(inventedHd, "Invented.Film.2020.1080p.mkv"));
        var inventedUhd = TestData.NewVersion(invented, "4k");
        inventedUhd.RootFolderId = uhdRoot.Id;
        db.MovieVersions.Add(inventedUhd);

        var (picture, _) = await TestData.SeedMovieAsync(db, tmdbId: 990102);
        Rename(picture, "An Invented Picture", 2018);
        picture.AlternativeTitles = [new AlternativeTitle("Le Film Inventé!", "fr")];

        var (classic, _) = await TestData.SeedMovieAsync(db, tmdbId: 990103);
        Rename(classic, "Invented Classic", 1975);
        classic.ImdbId = "tt0990103";

        var (sequel, _) = await TestData.SeedMovieAsync(db, tmdbId: 990104);
        Rename(sequel, "Invented Sequel", 2022);
        var sequelUhd = TestData.NewVersion(sequel, "4k");
        sequelUhd.RootFolderId = uhdRoot.Id;
        db.MovieVersions.Add(sequelUhd);

        var server = db.MediaServers.Add(new MediaServer
        {
            Name = "Invented Plex",
            Kind = MediaServerKind.Plex,
            Url = plexUrl,
            Token = FakePlex.Token,
            Enabled = false,
            PathMappings = [new PathMapping("/data", "/media")],
        }).Entity;
        await db.SaveChangesAsync();

        var at = DateTime.UnixEpoch.AddSeconds(T0);
        var owner = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "plex-owner", IsOwner = true, FirstSeenAt = at, LastSeenAt = at }).Entity;
        var item = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "40", Kind = MediaItemKind.Movie, Title = "Invented Sequel", FirstSeenAt = at, UpdatedAt = at }).Entity;
        await db.SaveChangesAsync();
        db.Plays.Add(new Play
        {
            UserId = owner.Id,
            ItemId = item.Id,
            StartedAt = DateTime.UnixEpoch.AddSeconds(T0 + 50_100),
            StoppedAt = DateTime.UnixEpoch.AddSeconds(T0 + 53_000),
            PlayedSeconds = 2900,
            MaxOffsetMs = 2_900_000,
            Source = PlaySource.Live,
        });
        await db.SaveChangesAsync();
        return (server.Id, new Films(invented.Id, picture.Id, classic.Id, sequel.Id));

        static void Rename(Movie movie, string title, int year)
        {
            movie.Title = title;
            movie.SortTitle = title.ToLowerInvariant();
            movie.Year = year;
        }
    }
}

/// <summary>Builds made-up Tautulli databases with Tautulli's table layout (a current one and an older, thinner one).</summary>
internal static class InventedTautulli
{
    private const long T0 = 1_700_000_000;

    public static void Create(string path)
    {
        Run(path, """
            CREATE TABLE version_info (key TEXT UNIQUE, value TEXT);
            INSERT INTO version_info VALUES ('version', 'v2.99.0');
            CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER DEFAULT NULL UNIQUE, username TEXT NOT NULL, friendly_name TEXT, thumb TEXT,
                email TEXT, is_admin INTEGER DEFAULT 0, keep_history INTEGER DEFAULT 1, deleted_user INTEGER DEFAULT 0, user_token TEXT, server_token TEXT);
            INSERT INTO users (user_id, username, friendly_name, thumb, email, is_admin, keep_history, deleted_user, user_token, server_token) VALUES
                (9001, 'owner-account', 'Owner', 'https://example.invalid/owner.png', 'owner@example.invalid', 1, 1, 0, 'token-a', 'token-b'),
                (9002, 'alice', 'Alice', NULL, 'alice@example.invalid', 0, 1, 0, 'token-c', NULL),
                (9003, 'bob', 'bob', NULL, NULL, 0, 0, 1, NULL, NULL),
                (9004, 'dora', 'Dora', NULL, NULL, 0, 1, 0, NULL, NULL);
            CREATE TABLE session_history (id INTEGER PRIMARY KEY AUTOINCREMENT, reference_id INTEGER, started INTEGER, stopped INTEGER, rating_key INTEGER,
                user_id INTEGER, user TEXT, ip_address TEXT, paused_counter INTEGER DEFAULT 0, player TEXT, product TEXT, product_version TEXT, platform TEXT,
                platform_version TEXT, profile TEXT, machine_id TEXT, bandwidth INTEGER, location TEXT, quality_profile TEXT, secure INTEGER, relayed INTEGER,
                parent_rating_key INTEGER, grandparent_rating_key INTEGER, media_type TEXT, section_id INTEGER, view_offset INTEGER DEFAULT 0);
            CREATE TABLE session_history_metadata (id INTEGER PRIMARY KEY, rating_key INTEGER, parent_rating_key INTEGER, grandparent_rating_key INTEGER,
                title TEXT, parent_title TEXT, grandparent_title TEXT, full_title TEXT, media_index INTEGER, parent_media_index INTEGER, media_type TEXT,
                year INTEGER, duration INTEGER DEFAULT 0, guid TEXT, live INTEGER DEFAULT 0, marker_credits_first INTEGER DEFAULT NULL, marker_credits_final INTEGER DEFAULT NULL,
                original_title TEXT);
            CREATE TABLE session_history_media_info (id INTEGER PRIMARY KEY, rating_key INTEGER, video_decision TEXT, audio_decision TEXT, transcode_decision TEXT,
                duration INTEGER DEFAULT 0, container TEXT, bitrate INTEGER, video_codec TEXT, video_resolution TEXT, video_dynamic_range TEXT, audio_codec TEXT,
                audio_channels INTEGER, transcode_hw_decoding INTEGER, transcode_hw_encoding INTEGER, stream_container TEXT, stream_bitrate INTEGER,
                stream_video_decision TEXT, stream_video_codec TEXT, stream_video_resolution TEXT, stream_audio_decision TEXT, stream_audio_codec TEXT,
                stream_audio_channels INTEGER, stream_subtitle_decision TEXT);
            CREATE TABLE themoviedb_lookup (id INTEGER PRIMARY KEY AUTOINCREMENT, rating_key INTEGER, thetvdb_id INTEGER, imdb_id TEXT, themoviedb_id INTEGER, themoviedb_url TEXT, themoviedb_json TEXT);
            INSERT INTO themoviedb_lookup (rating_key, themoviedb_id) VALUES (60, 990101);
            CREATE TABLE tvmaze_lookup (id INTEGER PRIMARY KEY AUTOINCREMENT, rating_key INTEGER, thetvdb_id INTEGER, imdb_id TEXT, tvmaze_id INTEGER, tvmaze_url TEXT, tvmaze_json TEXT);
            INSERT INTO tvmaze_lookup (rating_key, thetvdb_id) VALUES (800, 70001);
            """);

        // Alice watches the invented film over two sessions (one play), in the 1080p library.
        Row(path, 1, 1, T0, T0 + 2400, 10, 9002, "alice", 2_400_000, "movie", 3, paused: 0);
        Row(path, 2, 1, T0 + 3600, T0 + 3600 + 3300, 10, 9002, "alice", 5_700_000, "movie", 3, paused: 60);
        Metadata(path, [1, 2], 10, "Invented Film", 2020, 6_000_000, "com.plexapp.agents.themoviedb://990101?lang=en");
        MediaInfo(path, 1, "direct play", "1080", "SDR");
        MediaInfo(path, 2, "direct play", "1080", "SDR");

        // The owner, half of a film known by its French title only; an old row with no media info at all.
        Row(path, 3, 3, T0 + 10_000, T0 + 11_800, 20, 9001, "owner-account", 1_800_000, "movie", 3, location: "lan", player: "Bedroom TV");
        Metadata(path, [3], 20, "Le Film Inventé", 2018, 5_400_000, "local://20", credits: 4_800_000);

        // Bob, deleted since, saw a film by the legacy IMDb agent; no reference id, and a media info row from an older Tautulli.
        Row(path, 4, null, T0 + 20_000, T0 + 26_000, 30, 9003, "bob", 5_990_000, "movie", 3);
        Metadata(path, [4], 30, "Invented Classic", 1975, 6_000_000, "com.plexapp.agents.imdb://tt0990103?lang=en");
        Run(path, "UPDATE session_history_metadata SET original_title = 'Un classique inventé' WHERE id = 4");
        Run(path, "INSERT INTO session_history_media_info (id, rating_key, video_decision) VALUES (4, 30, 'direct play')");

        // A film only Plex can name, in the 4K library: transcoded for Alice, and the owner's view Bigarr followed live.
        Row(path, 5, 5, T0 + 30_000, T0 + 31_000, 40, 9002, "alice", 1_000_000, "movie", 7, ip: "198.51.100.20");
        Row(path, 6, 6, T0 + 50_000, T0 + 53_000, 40, 9001, "owner-account", 3_000_000, "movie", 7);
        Metadata(path, [5, 6], 40, "La Suite Inventée", 2022, 7_200_000, "plex://movie/invented-sequel");
        MediaInfo(path, 5, "transcode", "4k", "HDR10", hardware: true);

        // A film nobody knows: Plex has no such item any more.
        Row(path, 7, 7, T0 + 60_000, T0 + 61_000, 50, 9002, "alice", 1_000_000, "movie", 3);
        Metadata(path, [7], 50, "Unknown Invention", 2001, 6_000_000, "plex://movie/unknown-invention");

        // The invented film again, from the 4K library, known through Tautulli's TMDB lookup.
        Row(path, 8, 8, T0 + 70_000, T0 + 76_000, 60, 9001, "owner-account", 6_000_000, "movie", 7);
        Metadata(path, [8], 60, "Invented Film", 2020, 6_000_000, "plex://movie/invented-film");
        MediaInfo(path, 8, "direct play", "4k", "Dolby Vision");

        // Episodes: a legacy TVDB guid, a lookup on the show, and a show only Plex knows.
        Row(path, 9, 9, T0 + 80_000, T0 + 82_400, 801, 9003, "bob", 2_400_000, "episode", 4, parent: 810, grandparent: 800);
        Episode(path, 9, 801, 810, 800, "Invented Show", 1, 1, "com.plexapp.agents.thetvdb://70001/1/1?lang=en");
        Row(path, 10, 10, T0 + 90_000, T0 + 92_400, 802, 9002, "alice", 2_400_000, "episode", 4, parent: 810, grandparent: 800);
        Episode(path, 10, 802, 810, 800, "Invented Show", 1, 2, "plex://episode/invented-show-1-2");
        Row(path, 11, 11, T0 + 100_000, T0 + 101_000, 803, 9002, "alice", 1_000_000, "episode", 4, parent: 910, grandparent: 900);
        Episode(path, 11, 803, 910, 900, "Other Invented Show", 1, 3, "plex://episode/other-1-3");

        // Music: left out.
        Row(path, 12, 12, T0 + 110_000, T0 + 110_300, 70, 9001, "owner-account", 300_000, "track", 9);
        Run(path, "INSERT INTO session_history_metadata (id, rating_key, title, media_type, duration, guid) VALUES (12, 70, 'Invented Song', 'track', 300000, 'plex://track/song')");
    }

    /// <summary>An older Tautulli: no media info table, no lookups, no version, thinner tables.</summary>
    public static void CreateOld(string path) => Run(path, $"""
        CREATE TABLE users (id INTEGER PRIMARY KEY, user_id INTEGER UNIQUE, username TEXT NOT NULL, friendly_name TEXT);
        INSERT INTO users (user_id, username) VALUES (5001, 'carol');
        CREATE TABLE session_history (id INTEGER PRIMARY KEY, reference_id INTEGER, started INTEGER, stopped INTEGER, rating_key INTEGER, user_id INTEGER,
            user TEXT, paused_counter INTEGER, player TEXT, media_type TEXT, section_id INTEGER, view_offset INTEGER);
        CREATE TABLE session_history_metadata (id INTEGER PRIMARY KEY, rating_key INTEGER, title TEXT, media_type TEXT, year INTEGER, duration INTEGER, guid TEXT);
        INSERT INTO session_history VALUES (1, 1, {T0}, {T0 + 3000}, 5, 5001, 'carol', 0, 'Old TV', 'movie', 1, 3000000);
        INSERT INTO session_history VALUES (2, 2, {T0 + 5000}, {T0 + 6000}, 6, 5001, 'carol', 0, 'Old TV', 'episode', 2, 1000000);
        INSERT INTO session_history_metadata VALUES (1, 5, 'Old Invented Film', 'movie', 1990, 6000000, 'com.plexapp.agents.imdb://tt0990199');
        INSERT INTO session_history_metadata VALUES (2, 6, 'Old Invented Episode', 'episode', 1991, 1500000, 'com.plexapp.agents.thetvdb://70099/1/1');
        """);

    public static void AddRow(string path, long id, long referenceId, long started, long stopped, int ratingKey, int userId, long viewOffsetMs)
    {
        Row(path, id, referenceId, started, stopped, ratingKey, userId, "owner-account", viewOffsetMs, "movie", 3);
        Run(path, $"INSERT INTO session_history_metadata (id, rating_key, title, media_type, year, duration, guid, marker_credits_first) SELECT {id}, rating_key, title, media_type, year, duration, guid, marker_credits_first FROM session_history_metadata WHERE rating_key = {ratingKey} LIMIT 1");
    }

    private static void Row(string path, long id, long? referenceId, long started, long stopped, int ratingKey, int userId, string user, long viewOffsetMs, string mediaType, int section,
        int paused = 0, string location = "wan", string player = "Living room TV", string ip = "203.0.113.5", int? parent = null, int? grandparent = null) =>
        Run(path, $"""
            INSERT INTO session_history (id, reference_id, started, stopped, rating_key, user_id, user, ip_address, paused_counter, player, product, platform,
                platform_version, machine_id, bandwidth, location, secure, relayed, parent_rating_key, grandparent_rating_key, media_type, section_id, view_offset)
            VALUES ({id}, {(referenceId?.ToString() ?? "NULL")}, {started}, {stopped}, {ratingKey}, {userId}, '{user}', '{ip}', {paused}, '{player}', 'Plex for Android (TV)',
                'Android', '12', 'invented-device-{id}', 20000, '{location}', 1, 0, {(parent?.ToString() ?? "NULL")}, {(grandparent?.ToString() ?? "NULL")}, '{mediaType}', {section}, {viewOffsetMs})
            """);

    private static void Metadata(string path, long[] ids, int ratingKey, string title, int year, long duration, string guid, long? credits = null)
    {
        foreach (var id in ids)
        {
            Run(path, $"INSERT INTO session_history_metadata (id, rating_key, title, full_title, media_type, year, duration, guid, marker_credits_first) VALUES ({id}, {ratingKey}, '{title.Replace("'", "''")}', '{title.Replace("'", "''")}', 'movie', {year}, {duration}, '{guid}', {(credits?.ToString() ?? "NULL")})");
        }
    }

    private static void Episode(string path, long id, int ratingKey, int parent, int grandparent, string show, int season, int episode, string guid) =>
        Run(path, $"""
            INSERT INTO session_history_metadata (id, rating_key, parent_rating_key, grandparent_rating_key, title, grandparent_title, media_index, parent_media_index, media_type, year, duration, guid)
            VALUES ({id}, {ratingKey}, {parent}, {grandparent}, 'Episode {episode}', '{show}', {episode}, {season}, 'episode', 2021, 2700000, '{guid}')
            """);

    private static void MediaInfo(string path, long id, string decision, string resolution, string dynamicRange, bool hardware = false)
    {
        var transcode = decision == "transcode";
        Run(path, $"""
            INSERT INTO session_history_media_info (id, video_decision, audio_decision, transcode_decision, duration, container, bitrate, video_codec, video_resolution,
                video_dynamic_range, audio_codec, audio_channels, transcode_hw_decoding, transcode_hw_encoding, stream_container, stream_bitrate, stream_video_decision,
                stream_video_codec, stream_video_resolution, stream_audio_decision, stream_audio_codec, stream_audio_channels, stream_subtitle_decision)
            VALUES ({id}, '{decision}', 'direct play', '{decision}', 6000000, 'mkv', 20000, 'hevc', '{resolution}', '{dynamicRange}', 'eac3', 6,
                {(hardware ? 1 : 0)}, {(hardware ? 1 : 0)}, '{(transcode ? "mpegts" : "mkv")}', 8000, '{decision}', '{(transcode ? "h264" : "hevc")}',
                '{(transcode ? "1080" : resolution)}', 'direct play', 'eac3', 6, NULL)
            """);
    }

    private static void Run(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
