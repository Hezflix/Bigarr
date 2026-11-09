using Bigarr.Api.Features.Series;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Tests.Series;

public sealed class MonitoringRulesTests
{
    // Specials, two aired seasons, a third half aired; S01E02 and S02E01 have a file.
    private static readonly List<EpisodeFacts> Episodes =
    [
        new(1, 0, 1, HasAired: true, HasFile: false),
        new(10, 1, 1, true, false),
        new(11, 1, 2, true, true),
        new(20, 2, 1, true, true),
        new(21, 2, 2, true, false),
        new(30, 3, 1, true, false),
        new(31, 3, 2, false, false),
        new(32, 3, 3, false, false),
    ];

    [Theory]
    [InlineData(MonitoringOption.All, new[] { 10, 11, 20, 21, 30, 31, 32 })]
    [InlineData(MonitoringOption.Future, new[] { 31, 32 })]
    [InlineData(MonitoringOption.Missing, new[] { 10, 21, 30, 31, 32 })]
    [InlineData(MonitoringOption.Existing, new[] { 11, 20 })]
    [InlineData(MonitoringOption.Pilot, new[] { 10 })]
    [InlineData(MonitoringOption.FirstSeason, new[] { 10, 11 })]
    [InlineData(MonitoringOption.LatestSeason, new[] { 30, 31, 32 })]
    [InlineData(MonitoringOption.None, new int[0])]
    public void Options_pick_episodes_like_sonarr_and_never_specials(MonitoringOption option, int[] expected) =>
        Assert.Equal(expected, MonitoringRules.MonitoredEpisodes(option, Episodes).Order());

    [Theory]
    [InlineData(MonitoringOption.All, new[] { 1, 2, 3 })]
    [InlineData(MonitoringOption.Future, new[] { 3 })]
    [InlineData(MonitoringOption.Missing, new[] { 1, 2, 3 })]
    [InlineData(MonitoringOption.Existing, new[] { 1, 2 })]
    [InlineData(MonitoringOption.Pilot, new[] { 1 })]
    [InlineData(MonitoringOption.FirstSeason, new[] { 1 })]
    [InlineData(MonitoringOption.LatestSeason, new[] { 3 })]
    [InlineData(MonitoringOption.None, new int[0])]
    public void Season_flags_follow_the_episodes_and_the_open_ended_options_keep_the_latest_season(MonitoringOption option, int[] expected)
    {
        var monitored = MonitoringRules.MonitoredEpisodes(option, Episodes);
        var seasons = MonitoringRules.MonitoredSeasons(option, [0, 1, 2, 3], Episodes, monitored);

        Assert.Equal(expected, seasons.Where(s => s.Value).Select(s => s.Key).Order());
    }

    [Fact]
    public void Future_on_an_ended_show_still_watches_its_last_season_for_late_additions()
    {
        List<EpisodeFacts> ended = [new(1, 1, 1, true, false), new(2, 2, 1, true, false)];

        var monitored = MonitoringRules.MonitoredEpisodes(MonitoringOption.Future, ended);
        var seasons = MonitoringRules.MonitoredSeasons(MonitoringOption.Future, [1, 2], ended, monitored);

        Assert.Empty(monitored);
        Assert.Equal([2], seasons.Where(s => s.Value).Select(s => s.Key));
    }

    [Fact]
    public void A_show_with_only_specials_monitors_nothing()
    {
        List<EpisodeFacts> specials = [new(1, 0, 1, true, false)];

        Assert.Empty(MonitoringRules.MonitoredEpisodes(MonitoringOption.All, specials));
        Assert.Empty(MonitoringRules.MonitoredEpisodes(MonitoringOption.Pilot, specials));
    }

    [Fact]
    public void Pilot_is_the_lowest_episode_of_the_first_regular_season()
    {
        List<EpisodeFacts> unordered = [new(5, 2, 1, true, false), new(4, 1, 3, true, false), new(3, 1, 2, true, false)];

        Assert.Equal([3], MonitoringRules.MonitoredEpisodes(MonitoringOption.Pilot, unordered));
    }
}
