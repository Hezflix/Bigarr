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
/// The stats on a small invented history whose numbers are counted by hand: the Plays page's filters, a user's page,
/// the dashboard, and the Plex history backstop. "Now" is Saturday 14/03/2026 20:00 UTC.
/// <code>
///      user   item               version  start        played  streams
///  P1  alice  The Matrix         1080p    now-2h       3600    direct play, Living room TV, 192.168.1.40
///  P2  bob    The Matrix         4k       now-3d       7000    direct play then transcode, Laptop, 203.0.113.5
///  P3  bob    Made Up Show 1x01  -        now-10d      1800    direct stream, Phone, 198.51.100.7
///  P4  alice  The Matrix         unknown  now-40d      6000    direct play, Living room TV, 192.168.1.40 (1200 s paused)
///  P5  alice  Made Up Show 1x01  -        now-100d     3000    none (Plex's history)
/// </code>
/// </summary>
public sealed class PlaybackStatsTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly ServiceProvider _services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
    private FakePlex _plex = null!;
    private MediaServer _server = null!;
    private Movie _movie = null!;
    private MovieVersion _hd = null!;
    private MovieVersion _uhd = null!;
    private Play _p1 = null!;
    private Play _p2 = null!;
    private Play _p3 = null!;
    private Play _p4 = null!;
    private Play _p5 = null!;
    private MediaServerUser _alice = null!;
    private MediaServerUser _bob = null!;

    public async Task InitializeAsync()
    {
        _plex = await FakePlex.StartAsync();
        _plex.Items["100"] = new FakePlex.Item("100", "The Matrix", 1999, 136 * 60_000L, "3", [new FakePlex.Part("1001", "/media/movies/The Matrix (1999)/The.Matrix.1999.1080p.mkv", "1080")], TmdbId: 603);
        _plex.Items["300"] = new FakePlex.Item("300", "Pilot", 2021, 50 * 60_000L, "4", [new FakePlex.Part("3001", "/media/tv/Made Up Show/Season 01/Made.Up.Show.S01E01.mkv", "1080")], Type: "episode", ShowRatingKey: "30", ShowTitle: "Made Up Show", Season: 1, Episode: 1);
        _plex.ShowTvdbIds["30"] = 424242;

        await using var db = _database.CreateContext();
        (_movie, _hd) = await TestData.SeedMovieAsync(db);
        var uhdRoot = db.RootFolders.Add(new RootFolder { Path = "/data/movies4k" }).Entity;
        await db.SaveChangesAsync();
        _uhd = TestData.NewVersion(_movie, "4k");
        _uhd.RootFolderId = uhdRoot.Id;
        db.MovieVersions.Add(_uhd);
        _server = new MediaServer { Name = "Plex", Url = _plex.Url, Token = FakePlex.Token, PathMappings = [new PathMapping("/data", "/media")] };
        db.MediaServers.Add(_server);
        await db.SaveChangesAsync();

        _alice = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "1", Name = "alice", IsOwner = true, FirstSeenAt = Now.AddDays(-200), LastSeenAt = Now.AddHours(-1) }).Entity;
        _bob = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "2", Name = "bob", FirstSeenAt = Now.AddDays(-20), LastSeenAt = Now.AddDays(-3) }).Entity;
        var hdItem = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "100", Kind = MediaItemKind.Movie, Title = "The Matrix", Year = 1999, DurationMs = 136 * 60_000L, MovieId = _movie.Id, LibrarySectionId = "3", UpdatedAt = Now }).Entity;
        var uhdItem = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "200", Kind = MediaItemKind.Movie, Title = "The Matrix", Year = 1999, DurationMs = 136 * 60_000L, MovieId = _movie.Id, LibrarySectionId = "7", UpdatedAt = Now }).Entity;
        var episode = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = _server.Id, RatingKey = "300", Kind = MediaItemKind.Episode, Title = "Pilot", ShowTitle = "Made Up Show", SeasonNumber = 1, EpisodeNumber = 1, DurationMs = 50 * 60_000L, UpdatedAt = Now }).Entity;

        _p1 = Play(_alice, hdItem, Now.AddHours(-2), 3600, _hd, Stream(Now.AddHours(-2), 3600, StreamDecision.DirectPlay, "Living room TV", "Android", "192.168.1.40", local: true));
        _p2 = Play(_bob, uhdItem, Now.AddDays(-3), 7000, _uhd,
            Stream(Now.AddDays(-3), 3600, StreamDecision.DirectPlay, "Laptop", "Chrome", "203.0.113.5", resolution: "4k"),
            Stream(Now.AddDays(-3).AddHours(1), 3400, StreamDecision.Transcode, "Laptop", "Chrome", "203.0.113.5", resolution: "4k"));
        _p3 = Play(_bob, episode, Now.AddDays(-10), 1800, null, Stream(Now.AddDays(-10), 1800, StreamDecision.DirectStream, "Phone", "Android", "198.51.100.7"));
        _p4 = Play(_alice, hdItem, Now.AddDays(-40), 6000, null, Stream(Now.AddDays(-40), 7200, StreamDecision.DirectPlay, "Living room TV", "Android", "192.168.1.40", local: true, paused: 1200));
        _p4.VersionId = null;
        _p4.VersionName = null;
        _p5 = Play(_alice, episode, Now.AddDays(-100), 3000, null);
        _p5.Source = PlaySource.ServerHistory;
        db.Plays.AddRange(_p1, _p2, _p3, _p4, _p5);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Plays_filter_by_dates_version_decision_and_title()
    {
        await using var db = _database.CreateContext();
        var service = new PlaybackService(db, null!, new LiveTranscodes(), null!, null!, _time, NullLogger<PlaybackService>.Instance);
        async Task<List<int>> Ids(PlayFilter filter) =>
            (await service.PlaysAsync(1, 50, filter, PlaySort.StartedAt, false, CancellationToken.None)).Records.Select(r => r.Id).ToList();

        Assert.Equal([_p1.Id, _p2.Id], await Ids(new PlayFilter(From: Now.AddDays(-5))));
        Assert.Equal([_p2.Id, _p3.Id, _p4.Id, _p5.Id], await Ids(new PlayFilter(To: Now.AddDays(-2))));
        Assert.Equal([_p2.Id, _p3.Id], await Ids(new PlayFilter(From: Now.AddDays(-15), To: Now.AddDays(-2))));
        Assert.Equal([_p2.Id], await Ids(new PlayFilter(Version: "4k")));

        // A play is a transcode when any of its streams was; the unknown decision of Plex's history matches none.
        Assert.Equal([_p2.Id], await Ids(new PlayFilter(Decision: StreamDecision.Transcode)));
        Assert.Equal([_p3.Id], await Ids(new PlayFilter(Decision: StreamDecision.DirectStream)));
        Assert.Equal([_p1.Id, _p4.Id], await Ids(new PlayFilter(Decision: StreamDecision.DirectPlay)));

        // The title or the show's, any case; LIKE's wildcards are taken literally.
        Assert.Equal([_p3.Id, _p5.Id], await Ids(new PlayFilter(Search: "made UP")));
        Assert.Equal([_p1.Id, _p2.Id, _p4.Id], await Ids(new PlayFilter(Search: "matrix")));
        Assert.Empty(await Ids(new PlayFilter(Search: "%")));
        Assert.Equal([_p3.Id], await Ids(new PlayFilter(UserId: _bob.Id, Kind: MediaItemKind.Episode, Search: "pilot")));

        Assert.Equal(["1080p", "4k"], await service.VersionNamesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_users_page_counts_watch_time_players_and_decisions()
    {
        await using var db = _database.CreateContext();
        var service = new PlaybackService(db, null!, new LiveTranscodes(), null!, null!, _time, NullLogger<PlaybackService>.Instance);

        var alice = (await service.UserStatsAsync(_alice.Id, CancellationToken.None))!;
        Assert.Equal(
            [new WatchTimeResponse(1, 1, 3600), new WatchTimeResponse(7, 1, 3600), new WatchTimeResponse(30, 1, 3600), new WatchTimeResponse(null, 3, 12_600)],
            alice.WatchTime);
        Assert.Equal(Now.AddHours(-1), alice.LastPlayedAt);

        // P1's 3600 s and P4's 7200 s less 1200 s paused.
        var player = Assert.Single(alice.Players);
        Assert.Equal(new PlayerStatsResponse("Living room TV", "Android", 2, 9600, Now.AddHours(-1)), player);
        Assert.Equal(new DecisionCountsResponse(2, 0, 0, 1), alice.Decisions);

        var bob = (await service.UserStatsAsync(_bob.Id, CancellationToken.None))!;
        Assert.Equal(
            [new WatchTimeResponse(1, 0, 0), new WatchTimeResponse(7, 1, 7000), new WatchTimeResponse(30, 2, 8800), new WatchTimeResponse(null, 2, 8800)],
            bob.WatchTime);

        // Two streams of one play count one play on the laptop.
        Assert.Equal([("Laptop", 1, 7000L), ("Phone", 1, 1800L)], bob.Players.Select(p => (p.Player, p.Plays, p.Seconds)).ToList());
        Assert.Equal(new DecisionCountsResponse(0, 1, 1, 0), bob.Decisions);

        Assert.Null(await service.UserStatsAsync(9999, CancellationToken.None));
    }

    [Fact]
    public async Task A_display_name_and_keep_history_are_edited_and_a_blank_name_goes_back_to_the_servers()
    {
        await using (var db = _database.CreateContext())
        {
            var service = new PlaybackService(db, null!, new LiveTranscodes(), null!, null!, _time, NullLogger<PlaybackService>.Instance);
            var renamed = await service.UpdateUserAsync(_bob.Id, new UpdatePlaybackUserRequest("  Bobby  ", false), CancellationToken.None);
            Assert.True(renamed.IsSuccess);
            Assert.Equal("Bobby", renamed.Value!.DisplayName);
            Assert.False(renamed.Value.KeepHistory);
            Assert.Equal("Bobby", (await service.UsersAsync(CancellationToken.None)).Single(u => u.Id == _bob.Id).Name);

            var reset = await service.UpdateUserAsync(_bob.Id, new UpdatePlaybackUserRequest(" ", null), CancellationToken.None);
            Assert.Null(reset.Value!.DisplayName);
            Assert.False(reset.Value.KeepHistory);
            Assert.False((await service.UpdateUserAsync(9999, new UpdatePlaybackUserRequest(null, true), CancellationToken.None)).IsSuccess);
            Assert.False((await service.UpdateUserAsync(_bob.Id, new UpdatePlaybackUserRequest(new string('x', 257), null), CancellationToken.None)).IsSuccess);
        }

        await using var read = _database.CreateContext();
        Assert.Equal("bob", (await read.MediaServerUsers.SingleAsync(u => u.Id == _bob.Id)).ShownName);
    }

    [Fact]
    public async Task The_films_plays_list_its_versions_nobody_played()
    {
        await using (var db = _database.CreateContext())
        {
            var remux = TestData.NewVersion(await db.Movies.Include(m => m.Versions).SingleAsync(m => m.Id == _movie.Id), "remux");
            remux.FolderName = "The Matrix (1999) Remux";
            db.MovieVersions.Add(remux);
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var rows = await new PlaybackService(read, null!, new LiveTranscodes(), null!, null!, _time, NullLogger<PlaybackService>.Instance).MoviePlaysAsync(_movie.Id, CancellationToken.None);
        Assert.Equal([("1080p", 1), ("4k", 1), ("remux", 0), (null, 1)], rows.Select(r => (r.VersionName, r.Plays)).ToList());
        Assert.Null(rows.Single(r => r.VersionName == "remux").LastPlayedAt);
    }

    [Fact]
    public async Task The_dashboard_counts_the_last_30_days()
    {
        await using var db = _database.CreateContext();
        var stats = await new PlaybackStats(db, _time).StatsAsync(30, null, TimeZoneInfo.Utc, CancellationToken.None);

        // P1, P2 and P3: P4 is 40 days old.
        Assert.Equal(new StatsTotalsResponse(3, 12_400, 2, 2, 1), stats.Totals);
        Assert.Equal("day", stats.Bucket);
        Assert.Equal(31, stats.OverTime.Count);
        Assert.Equal(new DateOnly(2026, 2, 12), stats.OverTime[0].Date);
        Assert.Equal(new PlaysOverTimeResponse(new DateOnly(2026, 3, 14), 1, 0, 3600, DirectPlay: 1), stats.OverTime[^1]);
        Assert.Equal(new PlaysOverTimeResponse(new DateOnly(2026, 3, 11), 1, 0, 7000, Transcode: 1), stats.OverTime.Single(d => d.Date == new DateOnly(2026, 3, 11)));
        Assert.Equal(new PlaysOverTimeResponse(new DateOnly(2026, 3, 4), 0, 1, 1800, DirectStream: 1), stats.OverTime.Single(d => d.Date == new DateOnly(2026, 3, 4)));
        Assert.Equal(3, stats.OverTime.Sum(d => d.Movies + d.Episodes));

        Assert.Equal(new PlaysSplitResponse(1, 1, 0, 1, 0, 0), stats.ByHour[18]); // P1: a film, direct play
        Assert.Equal(new PlaysSplitResponse(2, 1, 1, 0, 1, 1), stats.ByHour[20]); // P2 (film, transcode) and P3 (episode, direct stream)
        Assert.Equal([0, 0, 2, 0, 0, 1, 0], stats.ByWeekday.Select(d => d.Plays)); // two Wednesdays, a Saturday
        Assert.Equal(new PlaysSplitResponse(2, 1, 1, 0, 1, 1), stats.ByWeekday[2]);
        Assert.Equal(new PlaysSplitResponse(1, 1, 0, 1, 0, 0), stats.ByWeekday[5]);
        Assert.Equal(24, stats.ByHour.Count);

        var movie = Assert.Single(stats.TopMovies);
        Assert.Equal(("The Matrix (1999)", _movie.Id, 2, 2, 10_600L), (movie.Title, movie.MovieId, movie.Plays, movie.Users, movie.Seconds));
        Assert.Equal(("Made Up Show", 1), (Assert.Single(stats.TopShows).Title, stats.TopShows[0].Plays));
        Assert.Equal([("bob", 2), ("alice", 1)], stats.TopUsers.Select(u => (u.Title, u.Plays)).ToList());
        Assert.Equal([("Android", 2), ("Chrome", 1)], stats.TopPlatforms.Select(p => (p.Title, p.Plays)).ToList());
        Assert.Equal(new DecisionCountsResponse(1, 1, 1, 0), stats.Decisions);

        // P2's two streams touch at the hour without overlapping.
        Assert.Equal(1, stats.Concurrency.Streams);

        var uhd = Assert.Single(stats.TranscodedUhd);
        Assert.Equal((_movie.Id, "4k", 1, 1), (uhd.MovieId, uhd.VersionName, uhd.Plays, uhd.Transcodes));
    }

    [Fact]
    public async Task The_dashboard_counts_in_the_viewers_time_zone_and_by_month_since_the_start()
    {
        await using var db = _database.CreateContext();
        var stats = await new PlaybackStats(db, _time).StatsAsync(null, null, PlaybackStats.Zone("Pacific/Kiritimati"), CancellationToken.None);

        // UTC+14: P1 started at 08:00 the next day, P2, P3 and P4 at 10:00, P5 at 10:00 too.
        Assert.Equal(5, stats.Totals.Plays);
        Assert.Equal("month", stats.Bucket);
        Assert.Equal(1, stats.ByHour[8].Plays);
        Assert.Equal(4, stats.ByHour[10].Plays);

        // P5, from Plex's history, has no stream: a play of the hour, in no decision.
        Assert.Equal(new PlaysSplitResponse(4, 2, 2, 1, 1, 1), stats.ByHour[10]);
        Assert.Equal([new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1)], stats.OverTime.Select(m => m.Date).ToList());
        Assert.Equal([1, 0, 1, 3], stats.OverTime.Select(m => m.Movies + m.Episodes).ToList());
        Assert.Equal(1, stats.Decisions.Unknown);
        Assert.Equal(TimeZoneInfo.Utc, PlaybackStats.Zone("Not/AZone"));
    }

    [Fact]
    public async Task Top_users_rank_by_plays_or_by_watch_time()
    {
        await using (var db = _database.CreateContext())
        {
            // erin: one long play yesterday, 20,000 s; bob has two plays (8800 s), alice one (3600 s) in the month.
            var erin = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "5", Name = "erin" }).Entity;
            var item = await db.MediaServerItems.SingleAsync(i => i.RatingKey == "100");
            db.Plays.Add(Play(erin, item, Now.AddDays(-1), 20_000, null));
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var stats = await new PlaybackStats(read, _time).StatsAsync(30, null, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal([("bob", 2), ("erin", 1), ("alice", 1)], stats.TopUsers.Select(u => (u.Title, u.Plays)).ToList());
        Assert.Equal([("erin", 20_000L), ("bob", 8800L), ("alice", 3600L)], stats.TopUsersByTime.Select(u => (u.Title, u.Seconds)).ToList());
    }

    [Fact]
    public async Task The_dashboard_shows_one_persons_numbers()
    {
        await using var db = _database.CreateContext();
        var stats = await new PlaybackStats(db, _time).StatsAsync(30, _bob.Id, TimeZoneInfo.Utc, CancellationToken.None);

        // bob's P2 and P3 only.
        Assert.Equal((_bob.Id, new StatsTotalsResponse(2, 8800, 1, 1, 1)), (stats.UserId, stats.Totals));
        Assert.Equal([("bob", 2)], stats.TopUsers.Select(u => (u.Title, u.Plays)).ToList());
        Assert.Equal(new DecisionCountsResponse(0, 1, 1, 0), stats.Decisions);
    }

    [Fact]
    public async Task Most_streams_at_once_counts_streams_running_together_whoever_plays_them_and_paused_ones_too()
    {
        await using (var db = _database.CreateContext())
        {
            var alice = await db.MediaServerUsers.SingleAsync(u => u.Id == _alice.Id);
            var bob = await db.MediaServerUsers.SingleAsync(u => u.Id == _bob.Id);
            var item = await db.MediaServerItems.SingleAsync(i => i.RatingKey == "300");
            var at = Now.AddDays(-1);

            // alice and bob at once, bob's paused for half of it: 2. alice then resumes on a second stream of the
            // same play right after the first one ends: still 2, never 3.
            db.Plays.AddRange(
                Play(alice, item, at, 1800, null, Stream(at, 1800, StreamDecision.DirectPlay, "TV", "Android", "192.0.2.1"), Stream(at.AddSeconds(1800), 1200, StreamDecision.DirectPlay, "TV", "Android", "192.0.2.1")),
                Play(bob, item, at.AddMinutes(10), 1500, null, Stream(at.AddMinutes(10), 3000, StreamDecision.DirectPlay, "Phone", "Android", "198.51.100.7", paused: 1500)));
            await db.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        var stats = await new PlaybackStats(read, _time).StatsAsync(7, null, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(2, stats.Concurrency.Streams);
        Assert.Equal(Now.AddDays(-1).AddMinutes(10), stats.Concurrency.At);
    }

    [Fact]
    public async Task Plex_history_adds_the_viewed_marks_bigarr_missed_once_and_from_its_checkpoint()
    {
        await using (var db = _database.CreateContext())
        {
            db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = _server.Id, ExternalId = "3", Name = "carol", KeepHistory = false, FirstSeenAt = Now, LastSeenAt = Now });
            await db.SaveChangesAsync();
        }

        // The first live play (P4, now-40d) is where the backstop starts.
        _plex.Accounts["4"] = "dave";
        _plex.History.AddRange(
        [
            new FakePlex.Viewed("100", "2", Now.AddDays(-60)), // before the first live play: the import's business
            new FakePlex.Viewed("100", "1", Now.AddHours(-1).AddMinutes(-30)), // alice's P1: covered (but too young, see below)
            new FakePlex.Viewed("300", "2", Now.AddDays(-10).AddMinutes(25)), // bob's P3: covered
            new FakePlex.Viewed("100", "2", Now.AddDays(-4)), // bob, missed
            new FakePlex.Viewed("300", "3", Now.AddDays(-3)), // carol keeps no history
            new FakePlex.Viewed("300", "4", Now.AddDays(-2)), // dave, never seen live: missed
        ]);

        var first = await BackstopAsync();
        Assert.Equal(new BackstopPass("Plex", 4, 2, null), first);

        await using (var db = _database.CreateContext())
        {
            var added = await db.Plays.Include(p => p.User).Include(p => p.Item).Where(p => p.Source == PlaySource.ServerHistory && p.Id != _p5.Id).OrderBy(p => p.StoppedAt).ToListAsync();
            Assert.Equal(2, added.Count);

            // bob's film: as long as the film, watched, on the version of the 1080p library.
            Assert.Equal(("bob", "100", Now.AddDays(-4)), (added[0].User.Name, added[0].Item.RatingKey, added[0].StoppedAt));
            Assert.Equal(Now.AddDays(-4).AddMinutes(-136), added[0].StartedAt);
            Assert.Equal((136 * 60, true, _movie.Id, _hd.Id, "1080p"), (added[0].PlayedSeconds, added[0].Watched, added[0].MovieId, added[0].VersionId, added[0].VersionName));
            Assert.Empty(await db.PlayStreams.Where(s => s.PlayId == added[0].Id).ToListAsync());

            // dave is learnt from the server's accounts.
            Assert.Equal(("dave", "300", false), (added[1].User.Name, added[1].Item.RatingKey, added[1].User.IsOwner));
            Assert.Equal(Now - PlexHistoryBackstop.Settle, (await db.MediaServers.SingleAsync(s => s.Id == _server.Id)).PlayHistoryCheckedUntil);
        }

        // Again a day later: from the checkpoint, nothing new but alice's mark, now settled and covered by P1.
        _time.Advance(TimeSpan.FromDays(1));
        _plex.History.Add(new FakePlex.Viewed("100", "2", Now.AddHours(10)));
        Assert.Equal(new BackstopPass("Plex", 2, 1, null), await BackstopAsync());
        Assert.Equal(1, _plex.Requests.Count(r => r.Path == "/accounts"));

        await using var final = _database.CreateContext();
        Assert.Equal(8, await final.Plays.CountAsync());
    }

    [Fact]
    public async Task Plex_history_waits_for_a_first_live_play_and_reports_a_server_that_fails()
    {
        await using (var db = _database.CreateContext())
        {
            db.Plays.RemoveRange(db.Plays);
            await db.SaveChangesAsync();
        }

        Assert.Equal(new BackstopPass("Plex", 0, 0, null), await BackstopAsync());
        Assert.DoesNotContain(_plex.Requests, r => r.Path == "/status/sessions/history/all");

        await using (var db = _database.CreateContext())
        {
            var server = await db.MediaServers.SingleAsync(s => s.Id == _server.Id);
            Assert.Equal(Now - PlexHistoryBackstop.Settle, server.PlayHistoryCheckedUntil);
            server.Token = "wrong";
            server.PlayHistoryCheckedUntil = Now.AddDays(-2);
            await db.SaveChangesAsync();
        }

        var failed = await BackstopAsync();
        Assert.NotNull(failed.Error);
        await using var read = _database.CreateContext();
        Assert.Equal(Now.AddDays(-2), (await read.MediaServers.SingleAsync(s => s.Id == _server.Id)).PlayHistoryCheckedUntil);
    }

    private async Task<BackstopPass> BackstopAsync()
    {
        await using var db = _database.CreateContext();
        var plex = new PlexClient(_services.GetRequiredService<IHttpClientFactory>());
        var libraries = new PlexLibraries(plex, _time, NullLogger<PlexLibraries>.Instance);
        var resolver = new VersionResolver(db);
        var tracker = new PlaybackTracker(db, plex, libraries, resolver, new LiveTranscodes(), _time, NullLogger<PlaybackTracker>.Instance);
        var passes = await new PlexHistoryBackstop(db, plex, tracker, libraries, resolver, _time, NullLogger<PlexHistoryBackstop>.Instance).RunAllAsync(CancellationToken.None);
        return Assert.Single(passes);
    }

    private static Play Play(MediaServerUser user, MediaServerItem item, DateTime start, int played, MovieVersion? version, params PlayStream[] streams) => new()
    {
        User = user,
        Item = item,
        StartedAt = start,
        StoppedAt = streams.Length > 0 ? streams.Max(s => s.StoppedAt) : start.AddSeconds(played),
        PlayedSeconds = played,
        Watched = true,
        MovieId = item.MovieId,
        VersionId = version?.Id,
        VersionName = version?.Name,
        Streams = [.. streams],
    };

    private static PlayStream Stream(DateTime start, int seconds, StreamDecision decision, string player, string platform, string ip, bool local = false, int paused = 0, string resolution = "1080") => new()
    {
        StartedAt = start,
        StoppedAt = start.AddSeconds(seconds),
        PausedSeconds = paused,
        Details = new StreamDetails { Decision = decision, PlayerTitle = player, PlayerPlatform = platform, IpAddress = ip, Local = local, SourceVideoResolution = resolution },
    };

    public async Task DisposeAsync() => await _plex.DisposeAsync();

    public void Dispose()
    {
        _database.Dispose();
        _services.Dispose();
    }
}
