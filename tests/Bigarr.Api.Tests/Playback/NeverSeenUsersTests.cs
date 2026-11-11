using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// The migration that gives "never seen" to the users an older Tautulli import dated at the import: it runs on rows
/// written before it, and leaves alone anyone the data cannot tell from a user really seen.
/// </summary>
public sealed class NeverSeenUsersTests : IDisposable
{
    private const string Before = "20261111163545_SonarrCustomFormats";

    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public void Only_the_users_an_import_stamped_and_who_never_played_lose_their_date()
    {
        var stamp = new DateTime(2026, 11, 10, 9, 30, 0, DateTimeKind.Utc).AddTicks(1234567);
        var live = new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc);
        Dictionary<string, int> ids;
        using (var db = _database.CreateContext())
        {
            db.GetService<IMigrator>().Migrate(Before);
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            db.SaveChanges();
            var item = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "1", Kind = MediaItemKind.Movie, Title = "Invented Film" }).Entity;
            MediaServerUser User(string name, DateTime first, DateTime last, bool keep = true) =>
                db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = name, Name = name, FirstSeenAt = first, LastSeenAt = last, KeepHistory = keep }).Entity;

            // An import's accounts without history: the same stamp, no play.
            User("imported-a", stamp, stamp);
            User("imported-b", stamp, stamp);

            // Same stamp but a play, or a stream open now: seen.
            var played = User("imported-played", stamp, stamp);
            var streaming = User("imported-streaming", stamp, stamp);

            // Seen live once with history off: dated at its own instant, nobody shares it.
            User("live-history-off", live, live, keep: false);

            // Seen live, later than first seen.
            User("live", live.AddDays(-3), live);
            db.SaveChanges();
            db.Plays.Add(new Play { UserId = played.Id, Item = item, StartedAt = live, StoppedAt = live.AddHours(1) });
            db.PlaybackSessions.Add(new PlaybackSession { MediaServerId = server.Id, SessionKey = "9", RatingKey = "1", UserId = streaming.Id, Item = item, StartedAt = live, LastSeenAt = live });
            db.SaveChanges();
            ids = db.MediaServerUsers.ToDictionary(u => u.Name, u => u.Id);
        }

        using (var db = _database.CreateContext())
        {
            db.Database.Migrate();
        }

        using var after = _database.CreateContext();
        var seen = after.MediaServerUsers.AsNoTracking().ToDictionary(u => u.Name, u => u.LastSeenAt);
        Assert.Null(seen["imported-a"]);
        Assert.Null(seen["imported-b"]);
        Assert.Equal(stamp, seen["imported-played"]);
        Assert.Equal(stamp, seen["imported-streaming"]);
        Assert.Equal(live, seen["live-history-off"]);
        Assert.Equal(live, seen["live"]);
        Assert.Equal(6, ids.Count);
    }

    public void Dispose() => _database.Dispose();
}
