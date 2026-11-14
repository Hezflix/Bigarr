using Bigarr.Api.Features.Shadow;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>A row reads "S03 pack (52 episodes)" only when a release on it is a whole-season pack within one season.</summary>
public sealed class SeriesComparePackSeasonTests
{
    [Fact]
    public void Pack_within_one_season_names_that_season()
    {
        var episodes = Enumerable.Range(1, 52).Select(n => (3, n)).ToList();

        Assert.Equal(3, SeriesCompareService.PackSeason(episodes, anyPack: true));
    }

    [Fact]
    public void Single_episodes_are_not_a_pack()
    {
        Assert.Null(SeriesCompareService.PackSeason([(3, 1), (3, 2)], anyPack: false));
    }

    [Fact]
    public void Pack_spanning_seasons_keeps_the_range()
    {
        Assert.Null(SeriesCompareService.PackSeason([(1, 7), (2, 1)], anyPack: true));
    }

    [Fact]
    public void No_episodes_is_no_pack()
    {
        Assert.Null(SeriesCompareService.PackSeason([], anyPack: true));
    }
}
