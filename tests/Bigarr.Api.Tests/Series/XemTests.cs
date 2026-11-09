using Bigarr.Api.Infrastructure.Xem;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Tests.Series;

public sealed class XemTests
{
    private static readonly FixedTimeProvider Time = new(SeriesEndpointsTests.Now);

    [Fact]
    public async Task The_client_reads_havemap_mappings_and_names_like_sonarr()
    {
        var client = XemFakes.RecordedClient();

        Assert.Contains(XemFakes.FrierenTvdbId, await client.GetMappedTvdbIdsAsync(CancellationToken.None));

        var mappings = await client.GetMappingsAsync(XemFakes.FrierenTvdbId, CancellationToken.None);
        Assert.Equal(38, mappings.Count);
        Assert.Contains(new XemMapping(2, 5, 5, 2, 5, 33), mappings);

        // "no show with the tvdb_id" is XEM's way of saying "no mapping": empty, not an error.
        Assert.Empty(await client.GetMappingsAsync(81189, CancellationToken.None));

        var names = await client.GetNamesAsync(CancellationToken.None);
        Assert.Contains(new XemName(XemFakes.FrierenTvdbId, "Sousou no Frieren", null), names);
        Assert.Contains(new XemName(XemFakes.FrierenTvdbId, "Sousou no Frieren S2", 2), names);
    }

    [Fact]
    public async Task Scene_numbers_are_written_onto_episodes_and_kept_when_xem_is_unreachable()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var series = new Bigarr.Domain.Tv.Series { Title = "Frieren", SortTitle = "frieren", TvdbId = XemFakes.FrierenTvdbId, DefaultRootFolder = new Bigarr.Domain.Library.RootFolder { Path = "/tv", Kind = Bigarr.Domain.Library.MediaKind.Series } };
        var season = new Season { Series = series, SeasonNumber = 2 };
        var episode = new Episode { Series = series, Season = season, SeasonNumber = 2, EpisodeNumber = 5, AbsoluteEpisodeNumber = 33, Title = "S2E5" };
        db.Series.Add(series);
        db.Episodes.Add(episode);
        await db.SaveChangesAsync();

        Assert.True(await XemFakes.Service(db, Time, XemFakes.RecordedClient()).RefreshSeriesAsync(series, [episode], CancellationToken.None));
        Assert.True(series.UseSceneNumbering);
        Assert.Equal((2, 5, 5), (episode.SceneSeasonNumber, episode.SceneEpisodeNumber, episode.SceneAbsoluteEpisodeNumber));
        await db.SaveChangesAsync();

        Assert.False(await XemFakes.Service(db, Time, XemFakes.UnreachableClient()).RefreshSeriesAsync(series, [episode], CancellationToken.None));
        Assert.Equal((2, 5, 5), (episode.SceneSeasonNumber, episode.SceneEpisodeNumber, episode.SceneAbsoluteEpisodeNumber));

        // Names: stored cleaned for the lookup; an unreachable XEM leaves the previous list in place.
        Assert.Equal(9, await XemFakes.Service(db, Time, XemFakes.RecordedClient()).RefreshNamesAsync(CancellationToken.None));
        Assert.Null(await XemFakes.Service(db, Time, XemFakes.UnreachableClient()).RefreshNamesAsync(CancellationToken.None));
        var stored = await db.SceneNames.Where(n => n.TvdbId == XemFakes.FrierenTvdbId).OrderBy(n => n.Title).ToListAsync();
        Assert.Contains(stored, n => n.CleanTitle == "sousounofrierens2" && n.SceneSeasonNumber == 2);
    }

    [Fact]
    public void Episodes_past_the_mapped_range_get_extrapolated_scene_numbers_marked_unverified()
    {
        List<Episode> episodes =
        [
            new() { SeasonNumber = 1, EpisodeNumber = 1, Title = "a" },
            new() { SeasonNumber = 1, EpisodeNumber = 2, Title = "b" },
            new() { SeasonNumber = 1, EpisodeNumber = 3, Title = "c" },
        ];

        // XEM maps only the first two episodes, shifted by one (a double pilot aired as one).
        XemNumbering.Apply(episodes, [new XemMapping(1, 1, 1, 1, 1, 1), new XemMapping(1, 2, 2, 1, 2, 2)]);
        Assert.False(episodes[1].UnverifiedSceneNumbering);
        Assert.True(episodes[2].UnverifiedSceneNumbering);
        Assert.Equal((1, 3), (episodes[2].SceneSeasonNumber, episodes[2].SceneEpisodeNumber));
    }
}
