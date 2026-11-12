using Bigarr.Api.Features.Playback;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// The stats' top shows carry the Bigarr series they are, matched like Now playing's artwork: the show's TVDB id first,
/// then TMDB, then IMDb. A show Bigarr does not follow has none.
/// </summary>
public sealed class PlaybackStatsTopShowsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task A_top_show_links_to_its_series_by_tvdb_then_tmdb_and_an_unknown_show_has_none()
    {
        int byTvdb;
        int byTmdb;
        await using (var db = _database.CreateContext())
        {
            var root = db.RootFolders.Add(new Bigarr.Domain.Library.RootFolder { Path = "/data/tv" }).Entity;
            await db.SaveChangesAsync();
            Bigarr.Domain.Tv.Series Series(string title, int? tvdb, int? tmdb) =>
                db.Series.Add(new Bigarr.Domain.Tv.Series { Title = title, SortTitle = title.ToLowerInvariant(), TvdbId = tvdb, TmdbId = tmdb, DefaultRootFolderId = root.Id }).Entity;

            // The TVDB id wins over another series holding the show's TMDB id.
            var tvdbSeries = Series("Followed Show", 777001, null);
            var decoy = Series("Decoy", null, 888001);
            var tmdbSeries = Series("Other Show", null, 888002);
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "alice", FirstSeenAt = Now, LastSeenAt = Now }).Entity;
            MediaServerItem Episode(string key, string show, int? tvdb, int? tmdb) =>
                db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = key, Kind = MediaItemKind.Episode, Title = "Pilot", ShowTitle = show, ShowTvdbId = tvdb, ShowTmdbId = tmdb, SeasonNumber = 1, EpisodeNumber = 1 }).Entity;
            var followed = Episode("1", "Followed Show", 777001, 888001);
            var other = Episode("2", "Other Show", null, 888002);
            var unknown = Episode("3", "Unknown Show", 999001, null);
            Play Play(MediaServerItem item, int hoursAgo) => new() { User = user, Item = item, StartedAt = Now.AddHours(-hoursAgo), StoppedAt = Now.AddHours(-hoursAgo).AddMinutes(45), PlayedSeconds = 2700 };
            db.Plays.AddRange(Play(followed, 1), Play(followed, 5), Play(followed, 9), Play(other, 2), Play(other, 6), Play(unknown, 3));
            await db.SaveChangesAsync();
            (byTvdb, byTmdb) = (tvdbSeries.Id, tmdbSeries.Id);
            Assert.NotEqual(decoy.Id, byTvdb);
        }

        await using var read = _database.CreateContext();
        var stats = await new PlaybackStats(read, new MutableTimeProvider(new DateTimeOffset(Now))).StatsAsync(30, null, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal(
            [("Followed Show", (int?)byTvdb), ("Other Show", byTmdb), ("Unknown Show", null)],
            stats.TopShows.Select(s => (s.Title, s.SeriesId)).ToList());
        Assert.All(stats.TopUsers, u => Assert.Null(u.SeriesId));
    }

    public void Dispose() => _database.Dispose();
}
