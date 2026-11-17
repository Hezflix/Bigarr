using Bigarr.Api.Features.Playback.Persistence;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// The one-time repair of live plays recorded on a stale opening offset: the stream gets its real start, the play its
/// real furthest point and watched flag, a play split off by the stale offset goes back into the one it continues, and
/// imported or server-history plays stay as they are. Invented rows only.
/// </summary>
public sealed class StaleStartRepairTests : IDisposable
{
    private static readonly DateTime Evening = new(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task The_migration_queues_the_repair()
    {
        await using var db = _database.CreateContext();
        Assert.Contains(StaleStartRepair.Name, await db.PendingRepairs.Select(r => r.Name).ToListAsync());
    }

    [Fact]
    public async Task Stale_starts_are_repaired_split_plays_joined_and_other_sources_left_alone_once()
    {
        Ids ids;
        await using (var db = _database.CreateContext())
        {
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            var bob = new MediaServerUser { MediaServer = server, ExternalId = "2", Name = "bob" };
            var carol = new MediaServerUser { MediaServer = server, ExternalId = "3", Name = "carol" };
            var episode = new MediaServerItem { MediaServer = server, RatingKey = "300", Kind = MediaItemKind.Episode, Title = "Pilot", DurationMs = Minutes(50) };
            var film = new MediaServerItem { MediaServer = server, RatingKey = "100", Kind = MediaItemKind.Movie, Title = "Invented Film", DurationMs = Minutes(100) };
            var other = new MediaServerItem { MediaServer = server, RatingKey = "400", Kind = MediaItemKind.Movie, Title = "Other Film", DurationMs = Minutes(100) };

            // Opened at 48 of 50 minutes, really played 4 → 24: watched by the stale start, so the next day's resume
            // became a play of its own.
            var split = Live(bob, episode, Evening, Minutes(20), Minutes(48), Minutes(24), max: Minutes(48), watched: true);
            var resume = Live(bob, episode, Evening.AddDays(1), Minutes(26), Minutes(24), Minutes(50), max: Minutes(50), watched: true);

            // Stopped at 28 % with a stale start at 96 %: one play, not watched.
            var stopped = Live(carol, film, Evening, Minutes(8), Minutes(96), Minutes(28), max: Minutes(96), watched: true);

            // Imported and server-history plays with the same shape are not live tracking's to repair.
            var imported = Live(bob, film, Evening.AddDays(-30), Minutes(10), Minutes(96), Minutes(28), max: Minutes(96), watched: true);
            imported.Source = PlaySource.Tautulli;
            var history = new Play { User = bob, Item = film, StartedAt = Evening.AddDays(-20), StoppedAt = Evening.AddDays(-20), MaxOffsetMs = Minutes(100), DurationMs = Minutes(100), Watched = true, Source = PlaySource.ServerHistory };

            // Two live plays without a stale stream: what live tracking decided stays.
            var first = Live(carol, other, Evening, Minutes(20), 0, Minutes(20), max: Minutes(20), watched: false);
            var again = Live(carol, other, Evening.AddDays(1), Minutes(30), 0, Minutes(30), max: Minutes(30), watched: false);

            // A seek back within tolerance is no stale start.
            var seek = Live(bob, other, Evening, Minutes(10), Minutes(40), Minutes(39) + 30_000, max: Minutes(45), watched: false);

            db.Plays.AddRange(split, resume, stopped, imported, history, first, again, seek);
            await db.SaveChangesAsync();
            ids = new Ids(split.Id, resume.Id, stopped.Id, imported.Id, history.Id, first.Id, again.Id, seek.Id);
        }

        await RunAsync();
        await AssertRepairedAsync(ids);

        // Idempotent: nothing left to repair, and the queued row is gone.
        await using (var db = _database.CreateContext())
        {
            Assert.Empty(db.PendingRepairs);
            var repair = new StaleStartRepair(db, NullLogger<StaleStartRepair>.Instance);
            Assert.Equal((0, 0), await repair.RepairAsync(CancellationToken.None));
        }

        await RunAsync();
        await AssertRepairedAsync(ids);
    }

    private async Task AssertRepairedAsync(Ids ids)
    {
        await using var db = _database.CreateContext();
        var plays = await db.Plays.Include(p => p.Streams).AsNoTracking().ToDictionaryAsync(p => p.Id);

        // The split play is whole again: both streams, real start, watched by the resume.
        Assert.False(plays.ContainsKey(ids.Resume));
        var joined = plays[ids.Split];
        Assert.Equal(2, joined.Streams.Count);
        Assert.Equal(Minutes(4), joined.Streams.OrderBy(s => s.StartedAt).First().StartOffsetMs);
        Assert.Equal(Evening, joined.StartedAt);
        Assert.Equal(Evening.AddDays(1).AddMinutes(26), joined.StoppedAt);
        Assert.Equal((20 + 26) * 60, joined.PlayedSeconds);
        Assert.Equal(Minutes(50), joined.MaxOffsetMs);
        Assert.True(joined.Watched);

        var stopped = plays[ids.Stopped];
        Assert.Equal(Minutes(28), stopped.MaxOffsetMs);
        Assert.False(stopped.Watched);
        Assert.Equal(Minutes(20), Assert.Single(stopped.Streams).StartOffsetMs);

        var imported = plays[ids.Imported];
        Assert.True(imported.Watched);
        Assert.Equal(Minutes(96), imported.MaxOffsetMs);
        Assert.Equal(Minutes(96), Assert.Single(imported.Streams).StartOffsetMs);
        Assert.True(plays[ids.History].Watched);

        Assert.True(plays.ContainsKey(ids.First));
        Assert.True(plays.ContainsKey(ids.Again));
        Assert.Equal(Minutes(40), Assert.Single(plays[ids.Seek].Streams).StartOffsetMs);
        Assert.Equal(Minutes(45), plays[ids.Seek].MaxOffsetMs);
    }

    private async Task RunAsync()
    {
        await using var db = _database.CreateContext();
        await new StaleStartRepair(db, NullLogger<StaleStartRepair>.Instance).RunAsync(CancellationToken.None);
    }

    private static Play Live(MediaServerUser user, MediaServerItem item, DateTime startedAt, long playedMs, long startMs, long endMs, long max, bool watched)
    {
        var stoppedAt = startedAt.AddMilliseconds(playedMs);
        return new Play
        {
            User = user,
            Item = item,
            StartedAt = startedAt,
            StoppedAt = stoppedAt,
            PlayedSeconds = (int)(playedMs / 1000),
            MaxOffsetMs = max,
            DurationMs = item.DurationMs,
            Watched = watched,
            Source = PlaySource.Live,
            Streams = [new PlayStream { StartedAt = startedAt, StoppedAt = stoppedAt, StartOffsetMs = startMs, EndOffsetMs = endMs }],
        };
    }

    private static long Minutes(int minutes) => minutes * 60_000L;

    private sealed record Ids(int Split, int Resume, int Stopped, int Imported, int History, int First, int Again, int Seek);

    public void Dispose() => _database.Dispose();
}
