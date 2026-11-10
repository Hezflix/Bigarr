using Bigarr.Api.Features.Playback;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// Plex streams into plays, against a fake Plex and a real database: the played time leaves pauses out, a restart of
/// Bigarr or of Plex loses nothing and doubles nothing, and each play lands on the version whose file was played.
/// </summary>
public sealed class PlaybackTrackerTests : IAsyncLifetime, IDisposable
{
    private const string MatrixFile = "The.Matrix.1999.1080p.BluRay.mkv";
    private const string Matrix4kFile = "The.Matrix.1999.2160p.UHD.BluRay.mkv";

    private readonly SqliteTestDatabase _database = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 3, 14, 20, 0, 0, TimeSpan.Zero));
    private readonly LiveTranscodes _transcodes = new();
    private readonly ServiceProvider _services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
    private FakePlex _plex = null!;
    private MediaServer _server = null!;
    private Movie _movie = null!;
    private MovieVersion _hd = null!;
    private MovieVersion _uhd = null!;

    public async Task InitializeAsync()
    {
        _plex = await FakePlex.StartAsync();
        _plex.Items["100"] = new FakePlex.Item("100", "The Matrix", 1999, Minutes(136), "3", [new FakePlex.Part("1001", $"/media/movies/The Matrix (1999)/{MatrixFile}", "1080")], TmdbId: 603, CreditsMs: Minutes(130));
        _plex.Items["200"] = new FakePlex.Item("200", "The Matrix", 1999, Minutes(136), "7", [new FakePlex.Part("2001", $"/media/movies4k/The Matrix (1999)/{Matrix4kFile}", "4k", ColorTrc: "smpte2084")], TmdbId: 603);
        _plex.Items["300"] = new FakePlex.Item("300", "Pilot", 2021, Minutes(50), "4", [new FakePlex.Part("3001", "/media/tv/Made Up Show/Season 01/Made.Up.Show.S01E01.mkv", "1080")], Type: "episode", ShowRatingKey: "30", ShowTitle: "Made Up Show", Season: 1, Episode: 1);
        _plex.ShowTvdbIds["30"] = 424242;

        await using var db = _database.CreateContext();
        (_movie, _hd) = await TestData.SeedMovieAsync(db);
        var uhdRoot = db.RootFolders.Add(new RootFolder { Path = "/data/movies4k" }).Entity;
        await db.SaveChangesAsync();
        _uhd = TestData.NewVersion(_movie, "4k");
        _uhd.RootFolderId = uhdRoot.Id;
        db.MovieVersions.Add(_uhd);
        await db.SaveChangesAsync();
        db.MediaFiles.Add(TestData.NewFile(_hd, MatrixFile));
        db.MediaFiles.Add(TestData.NewFile(_uhd, Matrix4kFile));
        _server = new MediaServer { Name = "Plex", Url = _plex.Url, Token = FakePlex.Token, PathMappings = [new PathMapping("/data", "/media")] };
        db.MediaServers.Add(_server);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Play_pause_resume_stop_is_one_play_of_the_played_time_on_the_version_of_the_file()
    {
        _plex.Set(new FakePlex.Stream("12", "s-aaa", "100", "1", "alice", "1001"));
        Assert.Equal(1, await ReconcileAsync());

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(await EventAsync("12", "100", "paused", Minutes(10)));
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await EventAsync("12", "100", "playing", Minutes(10)));
        _time.Advance(TimeSpan.FromMinutes(20));
        Assert.False(await EventAsync("12", "100", "playing", Minutes(30)));
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(await EventAsync("12", "100", "stopped", Minutes(30) + 30_000));

        await using var db = _database.CreateContext();
        Assert.Empty(db.PlaybackSessions);
        var play = await db.Plays.Include(p => p.Streams).Include(p => p.User).Include(p => p.Item).SingleAsync();
        Assert.Equal(30 * 60 + 30, play.PlayedSeconds);
        Assert.Equal(5 * 60, play.PausedSeconds);
        Assert.False(play.Watched);
        Assert.Equal(_movie.Id, play.MovieId);
        Assert.Equal(_hd.Id, play.VersionId);
        Assert.Equal("1080p", play.VersionName);
        Assert.Equal("alice", play.User.Name);
        Assert.True(play.User.IsOwner);
        Assert.Equal(603, play.Item.TmdbId);
        Assert.Equal(_movie.Id, play.Item.MovieId);
        Assert.Equal(Minutes(130), play.Item.CreditsStartMs);

        var stream = Assert.Single(play.Streams);
        Assert.Equal($"/data/movies/The Matrix (1999)/{MatrixFile}", stream.FilePath);
        Assert.Equal($"/media/movies/The Matrix (1999)/{MatrixFile}", stream.Details.ServerFilePath);
        Assert.Equal(StreamDecision.DirectPlay, stream.Details.Decision);
        Assert.Equal("1080", stream.Details.SourceVideoResolution);
        Assert.Equal("192.0.2.10", stream.Details.IpAddress);
        Assert.Equal("Living room TV", stream.Details.PlayerTitle);
    }

    [Fact]
    public async Task Two_people_starting_the_same_new_film_at_once_share_one_item()
    {
        _plex.Set(new FakePlex.Stream("12", "s-aaa", "100", "2", "bob", "1001"));
        _plex.Set(new FakePlex.Stream("13", "s-bbb", "100", "3", "carol", "1001"));
        Assert.Equal(2, await ReconcileAsync());

        await using var db = _database.CreateContext();
        Assert.Equal(2, await db.PlaybackSessions.CountAsync());
        Assert.Equal(1, await db.MediaServerItems.CountAsync(i => i.RatingKey == "100"));
    }

    [Fact]
    public async Task A_restart_of_bigarr_in_the_middle_keeps_the_session_and_a_vanished_stream_stops_where_last_seen()
    {
        _plex.Set(new FakePlex.Stream("12", "s-aaa", "100", "2", "bob", "1001", Transcode: true));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(40));
        await EventAsync("12", "100", "playing", Minutes(40));

        // Bigarr is down for an hour; the stream goes on, then Bigarr reads the list again: same session, no second one.
        _time.Advance(TimeSpan.FromHours(1));
        _plex.Set(new FakePlex.Stream("12", "s-aaa", "100", "2", "bob", "1001", ViewOffsetMs: Minutes(100), Transcode: true));
        await ReconcileAsync();
        await using (var db = _database.CreateContext())
        {
            var session = await db.PlaybackSessions.SingleAsync();
            Assert.Equal(Minutes(100), session.ViewOffsetMs);
            Assert.Equal(StreamDecision.Transcode, session.Details.Decision);
            Assert.Equal("1080", session.Details.SourceVideoResolution);
            Assert.Equal("720", session.Details.StreamVideoResolution);
            Assert.True(session.Details.HardwareTranscode);
            var transcode = _transcodes.Get(_server.Id, "12");
            Assert.NotNull(transcode);
            Assert.Equal(1.8, transcode.Speed);
            Assert.True(transcode.Throttled);
            Assert.Equal(90_000, transcode.BufferedMs);
            Assert.Equal("Intel (QuickSync)", transcode.HardwareEncoding);
            Assert.Empty(db.Plays);
        }

        // The stream ended while nobody listened: it stops at the last time it was seen, and reaches the credits.
        _plex.Set(new FakePlex.Stream("12", "s-aaa", "100", "2", "bob", "1001", ViewOffsetMs: Minutes(131), Transcode: true));
        _time.Advance(TimeSpan.FromMinutes(31));
        await ReconcileAsync();
        _plex.Remove("12");
        _time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, await ReconcileAsync());
        Assert.Null(_transcodes.Get(_server.Id, "12"));

        await using var after = _database.CreateContext();
        var play = await after.Plays.SingleAsync();
        Assert.Equal((40 + 60 + 31) * 60, play.PlayedSeconds);
        Assert.True(play.Watched);
        Assert.Empty(after.PlaybackSessions);
    }

    [Fact]
    public async Task A_restart_of_plex_reusing_the_session_key_ends_the_old_stream_and_starts_a_new_one()
    {
        _plex.Set(new FakePlex.Stream("1", "s-before", "100", "2", "bob", "1001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(20));
        await EventAsync("1", "100", "playing", Minutes(20));

        _plex.Set(new FakePlex.Stream("1", "s-after", "100", "2", "bob", "1001", ViewOffsetMs: Minutes(21)));
        _time.Advance(TimeSpan.FromMinutes(3));
        await ReconcileAsync();

        await using var db = _database.CreateContext();
        var session = await db.PlaybackSessions.SingleAsync();
        Assert.Equal("s-after", session.ServerSessionId);
        var play = await db.Plays.Include(p => p.Streams).SingleAsync();
        Assert.Equal(20 * 60, play.PlayedSeconds);
    }

    [Fact]
    public async Task A_short_session_is_no_play_and_a_resumed_view_continues_the_unfinished_play()
    {
        // Under two minutes: nothing.
        _plex.Set(new FakePlex.Stream("5", "s-1", "200", "2", "bob", "2001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromSeconds(90));
        await EventAsync("5", "200", "stopped", 90_000);
        await using (var db = _database.CreateContext())
        {
            Assert.Empty(db.Plays);
        }

        // An evening's first hour, then the rest the next day: one play of two streams, watched.
        _plex.Set(new FakePlex.Stream("6", "s-2", "200", "2", "bob", "2001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(60));
        await EventAsync("6", "200", "stopped", Minutes(60));

        _time.Advance(TimeSpan.FromHours(20));
        _plex.Set(new FakePlex.Stream("7", "s-3", "200", "2", "bob", "2001", ViewOffsetMs: Minutes(60)));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(76));
        await EventAsync("7", "200", "stopped", Minutes(136) - 4_000);

        await using var after = _database.CreateContext();
        var play = await after.Plays.Include(p => p.Streams).SingleAsync();
        Assert.Equal(2, play.Streams.Count);
        Assert.Equal((60 + 76) * 60, play.PlayedSeconds);
        Assert.Equal(Minutes(136), play.MaxOffsetMs);
        Assert.True(play.Watched);
        Assert.Equal(_uhd.Id, play.VersionId);
        Assert.Equal("HDR10", play.Streams[0].Details.SourceDynamicRange);

        // Watched: the next view is a new play.
        _plex.Set(new FakePlex.Stream("8", "s-4", "200", "2", "bob", "2001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await EventAsync("8", "200", "stopped", Minutes(5));
        await using var last = _database.CreateContext();
        Assert.Equal(2, await last.Plays.CountAsync());
    }

    [Fact]
    public async Task Without_a_known_file_the_library_tells_the_version_and_the_minimum_can_be_a_share()
    {
        _plex.Items["200"] = _plex.Items["200"] with { Parts = [new FakePlex.Part("2001", "/somewhere/else.mkv", "4k")] };
        await using (var db = _database.CreateContext())
        {
            db.PlaybackSettings.Add(new PlaybackSettings { MinimumPlay = 10, MinimumPlayUnit = MinimumPlayUnit.Percent });
            await db.SaveChangesAsync();
        }

        _plex.Set(new FakePlex.Stream("9", "s-9", "200", "3", "carol", "2001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(13));
        await EventAsync("9", "200", "stopped", Minutes(13));
        await using (var db = _database.CreateContext())
        {
            Assert.Empty(db.Plays);
        }

        _plex.Set(new FakePlex.Stream("10", "s-10", "200", "3", "carol", "2001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(14));
        await EventAsync("10", "200", "stopped", Minutes(14));

        await using var after = _database.CreateContext();
        var play = await after.Plays.Include(p => p.Streams).SingleAsync();
        Assert.Equal(_uhd.Id, play.VersionId);
        Assert.Equal("/somewhere/else.mkv", play.Streams[0].FilePath);
    }

    [Fact]
    public async Task An_item_linked_by_hand_keeps_its_film_and_version_through_a_refresh_and_new_plays()
    {
        // A film whose ids Plex got wrong (another film's TMDB id) and whose file Bigarr does not know.
        _plex.Items["400"] = new FakePlex.Item("400", "Matrix (VF)", 1999, Minutes(136), "3", [new FakePlex.Part("4001", "/elsewhere/matrix.vf.mkv", "1080")], TmdbId: 999001);
        await using (var db = _database.CreateContext())
        {
            db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "400", Kind = MediaItemKind.Movie, Title = "Matrix (VF)", MovieId = _movie.Id, LinkedByHand = true, LinkedVersionId = _uhd.Id });
            await db.SaveChangesAsync();
        }

        _plex.Set(new FakePlex.Stream("40", "s-40", "400", "1", "alice", "4001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(40));
        await EventAsync("40", "400", "stopped", Minutes(40));

        await using var after = _database.CreateContext();
        var item = await after.MediaServerItems.SingleAsync(i => i.RatingKey == "400");
        Assert.Equal(_movie.Id, item.MovieId);
        Assert.Equal(999001, item.TmdbId);
        var play = await after.Plays.SingleAsync(p => p.ItemId == item.Id);
        Assert.Equal(_movie.Id, play.MovieId);
        Assert.Equal(_uhd.Id, play.VersionId);
        Assert.Equal("4k", play.VersionName);
    }

    [Fact]
    public async Task An_episode_keeps_the_show_ids_and_numbers_and_the_next_episode_on_the_same_player_is_a_new_stream()
    {
        _plex.Items["301"] = _plex.Items["300"] with { RatingKey = "301", Title = "Second", Episode = 2, Parts = [new FakePlex.Part("3011", null, "1080")] };
        _plex.Set(new FakePlex.Stream("20", "s-20", "300", "2", "bob", "3001"));
        await ReconcileAsync();
        _time.Advance(TimeSpan.FromMinutes(49));
        await EventAsync("20", "300", "playing", Minutes(49));

        // Autoplay: same session key, next rating key. The event is about an unknown stream, so the list is read.
        Assert.True(await EventAsync("20", "301", "playing", 0));
        _plex.Set(new FakePlex.Stream("20", "s-20", "301", "2", "bob", "3011"));
        await ReconcileAsync();

        await using var db = _database.CreateContext();
        var play = await db.Plays.Include(p => p.Item).SingleAsync();
        Assert.Equal(MediaItemKind.Episode, play.Item.Kind);
        Assert.Equal("Made Up Show", play.Item.ShowTitle);
        Assert.Equal(424242, play.Item.ShowTvdbId);
        Assert.Equal(1, play.Item.SeasonNumber);
        Assert.Equal(1, play.Item.EpisodeNumber);
        Assert.Null(play.MovieId);
        Assert.Equal("301", (await db.PlaybackSessions.SingleAsync()).RatingKey);
    }

    [Fact]
    public async Task Plays_sort_by_any_column_both_ways_and_keep_newest_first_as_the_default()
    {
        await using (var db = _database.CreateContext())
        {
            var alice = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "1", Name = "alice" }).Entity;
            var bob = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "2", Name = "bob" }).Entity;
            var film = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "100", Title = "Zeta" }).Entity;
            var other = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "200", Title = "Alpha" }).Entity;
            var at = _time.GetUtcNow().UtcDateTime;
            db.Plays.AddRange(
                new Play { User = bob, Item = film, StartedAt = at.AddHours(-1), StoppedAt = at, PlayedSeconds = 600, VersionName = "4k" },
                new Play { User = alice, Item = other, StartedAt = at.AddHours(-3), StoppedAt = at, PlayedSeconds = 6000, VersionName = "1080p" },
                new Play { User = alice, Item = film, StartedAt = at.AddHours(-2), StoppedAt = at, PlayedSeconds = 60 });
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var service = new PlaybackService(read, null!, _transcodes, null!, null!, _time, NullLogger<PlaybackService>.Instance);
        async Task<List<int>> Order(PlaySort sort, bool ascending) =>
            (await service.PlaysAsync(1, 50, new PlayFilter(), sort, ascending, CancellationToken.None)).Records.Select(r => r.PlayedSeconds).ToList();

        Assert.Equal([600, 60, 6000], await Order(PlaySort.StartedAt, false));
        Assert.Equal([6000, 60, 600], await Order(PlaySort.StartedAt, true));
        Assert.Equal([60, 6000, 600], await Order(PlaySort.User, true));
        Assert.Equal([6000, 600, 60], await Order(PlaySort.Title, true));
        Assert.Equal([6000, 600, 60], await Order(PlaySort.Played, false));
        Assert.Equal([60, 600, 6000], await Order(PlaySort.Played, true));
        Assert.Equal([600, 6000, 60], await Order(PlaySort.Version, false));
    }

    [Fact]
    public void Play_rules_follow_tautulli()
    {
        var rules = new PlayRules(new PlaybackSettings());
        Assert.Equal(120, rules.MinimumSeconds(Minutes(100)));
        Assert.Equal(600, new PlayRules(new PlaybackSettings { MinimumPlay = 10, MinimumPlayUnit = MinimumPlayUnit.Percent }).MinimumSeconds(Minutes(100)));
        Assert.Equal(120, new PlayRules(new PlaybackSettings { MinimumPlay = 10, MinimumPlayUnit = MinimumPlayUnit.Percent }).MinimumSeconds(null));

        Assert.True(rules.IsWatched(Minutes(90), Minutes(100), null));
        Assert.False(rules.IsWatched(Minutes(89), Minutes(100), null));
        Assert.True(rules.IsWatched(Minutes(85), Minutes(100), Minutes(84)));
        Assert.False(rules.IsWatched(Minutes(85), Minutes(100), Minutes(95)));

        Assert.Equal(Minutes(100), PlayRules.SnapEnd(Minutes(100) - 9_000, Minutes(100)));
        Assert.Equal(Minutes(100) - 11_000, PlayRules.SnapEnd(Minutes(100) - 11_000, Minutes(100)));

        Assert.True(PlayRules.Continues(new Play { Watched = false, MaxOffsetMs = Minutes(40) }, Minutes(41)));
        Assert.False(PlayRules.Continues(new Play { Watched = false, MaxOffsetMs = Minutes(40) }, Minutes(39)));
        Assert.False(PlayRules.Continues(new Play { Watched = true, MaxOffsetMs = Minutes(40) }, Minutes(90)));
    }

    [Fact]
    public void Server_paths_come_back_through_the_mappings_and_ids_come_from_any_guid()
    {
        var server = new MediaServer { Name = "Plex", Url = "http://plex", Token = "t", PathMappings = [new PathMapping("/data", "/media")] };
        Assert.Equal("/data/movies/A/a.mkv", server.TranslateFromServer("/media/movies/A/a.mkv"));
        Assert.Equal("/mediaother/a.mkv", server.TranslateFromServer("/mediaother/a.mkv"));

        using var legacy = System.Text.Json.JsonDocument.Parse("""{"Guid":[]}""");
        Assert.Equal(new PlexIds(null, "tt0133093", null), PlexPlayback.IdsOf(legacy.RootElement, "com.plexapp.agents.imdb://tt0133093?lang=en"));
        Assert.Equal(new PlexIds(603, null, null), PlexPlayback.IdsOf(legacy.RootElement, "com.plexapp.agents.themoviedb://603?lang=en"));
        Assert.Equal(new PlexIds(null, null, 81189), PlexPlayback.IdsOf(legacy.RootElement, "com.plexapp.agents.thetvdb://81189/1/2?lang=en"));

        Assert.Equal(new PlexPlayingEvent("7", "42", "paused", 1000), PlexPlayback.ParsePlayingEvent("""{"PlaySessionStateNotification":{"sessionKey":"7","ratingKey":"42","viewOffset":1000,"state":"paused"}}"""));
        Assert.Equal(new PlexPlayingEvent("7", "42", "playing", 5), PlexPlayback.ParsePlayingEvent("""{"NotificationContainer":{"type":"playing","PlaySessionStateNotification":[{"sessionKey":"7","ratingKey":"42","viewOffset":5,"state":"playing"}]}}"""));
        Assert.Null(PlexPlayback.ParsePlayingEvent("""{"TimelineEntry":{}}"""));
    }

    private async Task<int> ReconcileAsync()
    {
        await using var db = _database.CreateContext();
        var plex = new PlexClient(_services.GetRequiredService<IHttpClientFactory>());
        var live = await plex.SessionsAsync(_server, CancellationToken.None);
        return await Tracker(db, plex).ReconcileAsync(_server, live, CancellationToken.None);
    }

    private async Task<bool> EventAsync(string sessionKey, string ratingKey, string state, long offsetMs)
    {
        await using var db = _database.CreateContext();
        var plex = new PlexClient(_services.GetRequiredService<IHttpClientFactory>());
        return await Tracker(db, plex).OnEventAsync(_server, new PlexPlayingEvent(sessionKey, ratingKey, state, offsetMs), CancellationToken.None);
    }

    private PlaybackTracker Tracker(BigarrDbContext db, PlexClient plex) =>
        new(db, plex, new PlexLibraries(plex, _time, NullLogger<PlexLibraries>.Instance), new VersionResolver(db), _transcodes, _time, NullLogger<PlaybackTracker>.Instance);

    private static long Minutes(int minutes) => minutes * 60_000L;

    public async Task DisposeAsync() => await _plex.DisposeAsync();

    public void Dispose()
    {
        _database.Dispose();
        _services.Dispose();
    }
}
