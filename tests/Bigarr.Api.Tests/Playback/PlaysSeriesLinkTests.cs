using Bigarr.Api.Features.Playback;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// An episode's play carries the Bigarr series its show is, found like the stats' top shows and Now playing's artwork
/// (TVDB id, then TMDB, then IMDb), so the Plays list can link to the series page. Films and unfollowed shows have none.
/// </summary>
public sealed class PlaysSeriesLinkTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();

    [Fact]
    public async Task An_episode_play_names_its_series_and_a_film_or_an_unfollowed_show_has_none()
    {
        int byTvdb;
        int byImdb;
        await using (var db = _database.CreateContext())
        {
            var root = db.RootFolders.Add(new Bigarr.Domain.Library.RootFolder { Path = "/data/tv" }).Entity;
            await db.SaveChangesAsync();
            Bigarr.Domain.Tv.Series Series(string title, int? tvdb, string? imdb) =>
                db.Series.Add(new Bigarr.Domain.Tv.Series { Title = title, SortTitle = title.ToLowerInvariant(), TvdbId = tvdb, ImdbId = imdb, DefaultRootFolderId = root.Id }).Entity;
            var tvdbSeries = Series("Followed Show", 777001, null);
            var imdbSeries = Series("Other Show", null, "tt7770002");
            var server = db.MediaServers.Add(new MediaServer { Name = "Plex", Url = "http://localhost:1", Token = "t", Enabled = false }).Entity;
            await db.SaveChangesAsync();
            var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "alice", FirstSeenAt = Now, LastSeenAt = Now }).Entity;
            MediaServerItem Episode(string key, string show, int? tvdb, string? imdb) =>
                db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = key, Kind = MediaItemKind.Episode, Title = "Pilot", ShowTitle = show, ShowTvdbId = tvdb, ShowImdbId = imdb, SeasonNumber = 1, EpisodeNumber = 1 }).Entity;
            var film = db.MediaServerItems.Add(new MediaServerItem { MediaServerId = server.Id, RatingKey = "f", Kind = MediaItemKind.Movie, Title = "A Film", TmdbId = 1 }).Entity;
            Play Play(MediaServerItem item, int hoursAgo) => new() { User = user, Item = item, StartedAt = Now.AddHours(-hoursAgo), StoppedAt = Now.AddHours(-hoursAgo).AddMinutes(45), PlayedSeconds = 2700 };
            db.Plays.AddRange(
                Play(Episode("1", "Followed Show", 777001, null), 1),
                Play(Episode("2", "Other Show", null, "tt7770002"), 2),
                Play(Episode("3", "Unknown Show", 999001, null), 3),
                Play(film, 4));
            await db.SaveChangesAsync();
            (byTvdb, byImdb) = (tvdbSeries.Id, imdbSeries.Id);
        }

        await using var read = _database.CreateContext();
        var service = new PlaybackService(read, null!, new LiveTranscodes(), null!, null!, new MutableTimeProvider(new DateTimeOffset(Now)), NullLogger<PlaybackService>.Instance);
        var page = await service.PlaysAsync(1, 50, new PlayFilter(), PlaySort.StartedAt, false, CancellationToken.None);

        Assert.Equal(
            [("Pilot", (int?)byTvdb), ("Pilot", byImdb), ("Pilot", null), ("A Film", null)],
            page.Records.Select(r => (r.Item.Title, r.SeriesId)).ToList());
    }

    public void Dispose() => _database.Dispose();
}
