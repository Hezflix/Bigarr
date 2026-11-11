namespace Bigarr.Api.Tests.Series;

using System.Globalization;
using Bigarr.Domain.Tv;

/// <summary>
/// Air times against Sonarr's: every case is a show's air time, its network's country, its own country and an episode's
/// air date as TVDB gives them, with the <c>airDateUtc</c> Sonarr's metadata proxy (Skyhook) returned for that episode,
/// recorded on 2026-11-11. The network's clock wins over the show's country (The Crown), daylight saving follows the
/// zone (Breaking Bad in January and July, Bluey across Sydney's change, Doctor Who across London's), and a time after
/// midnight is the day before in UTC (Vinland Saga).
/// </summary>
public sealed class AirTimesTests
{
    [Theory]
    [InlineData("Breaking Bad S01E01", "2008-01-20", "21:00", "us", "us", "2008-01-21T02:00:00Z")]
    [InlineData("Breaking Bad S04E01", "2011-07-17", "21:00", "us", "us", "2011-07-18T01:00:00Z")]
    [InlineData("Game of Thrones S01E01", "2011-04-17", "21:00", "us", "us", "2011-04-18T01:00:00Z")]
    [InlineData("Stranger Things S01E01", "2016-07-15", "20:00", "us", "us", "2016-07-16T00:00:00Z")]
    [InlineData("Stranger Things S05E05", "2025-12-25", "20:00", "us", "us", "2025-12-26T01:00:00Z")]
    [InlineData("The Crown S01E01", "2016-11-04", "08:00", "us", "gb", "2016-11-04T12:00:00Z")]
    [InlineData("The Crown S02E01", "2017-12-08", "08:00", "us", "gb", "2017-12-08T13:00:00Z")]
    [InlineData("Bluey S01E01", "2018-10-01", "08:00", "au", "au", "2018-09-30T22:00:00Z")]
    [InlineData("Bluey S01E52", "2019-12-12", "08:00", "au", "au", "2019-12-11T21:00:00Z")]
    [InlineData("Vinland Saga S01E01", "2019-07-08", "00:30", "jp", "jp", "2019-07-07T15:30:00Z")]
    [InlineData("Frieren S01E01", "2023-09-29", "23:00", "jp", "jp", "2023-09-29T14:00:00Z")]
    [InlineData("Doctor Who S01E01", "2005-03-26", "18:30", "gb", "gb", "2005-03-26T18:30:00Z")]
    [InlineData("Doctor Who S02E12", "2006-07-01", "18:30", "gb", "gb", "2006-07-01T17:30:00Z")]
    [InlineData("The Daily Show S01E01", "1996-07-22", "23:00", "us", "us", "1996-07-23T03:00:00Z")]
    [InlineData("The Daily Show S31E130", "2026-11-12", "23:00", "us", "us", "2026-11-13T04:00:00Z")]
    public void Matches_sonarrs_air_date_utc(string episode, string airDate, string airTime, string networkCountry, string country, string expected)
    {
        var utc = AirTimes.ToUtc(DateOnly.Parse(airDate, CultureInfo.InvariantCulture), airTime, networkCountry, country);

        Assert.True(utc is not null, episode);
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), utc!.Value);
        Assert.Equal(DateTimeKind.Utc, utc.Value.Kind);
    }

    [Fact]
    public void Without_the_networks_country_the_shows_decides_and_without_either_the_time_is_utc()
    {
        var date = new DateOnly(2023, 9, 29);
        Assert.Equal(new DateTime(2023, 9, 29, 14, 0, 0, DateTimeKind.Utc), AirTimes.ToUtc(date, "23:00", null, "jp"));
        Assert.Equal(new DateTime(2023, 9, 29, 23, 0, 0, DateTimeKind.Utc), AirTimes.ToUtc(date, "23:00", null, null));
        Assert.Equal(new DateTime(2023, 9, 29, 23, 0, 0, DateTimeKind.Utc), AirTimes.ToUtc(date, "23:00", "zz", "zz"));
        Assert.Equal(new DateTime(2023, 9, 29, 4, 0, 0, DateTimeKind.Utc), AirTimes.ToUtc(date, null, "us", "us")); // no time: midnight local
        Assert.Null(AirTimes.ToUtc(null, "23:00", "us", "us"));
    }

    [Fact]
    public void A_time_the_spring_change_skips_is_read_an_hour_later()
    {
        // 02:30 does not exist in New York on 2024-03-10.
        Assert.Equal(new DateTime(2024, 3, 10, 7, 30, 0, DateTimeKind.Utc), AirTimes.ToUtc(new DateOnly(2024, 3, 10), "02:30", "us", "us"));
    }

    [Fact]
    public void Same_day_episodes_are_spread_one_runtime_apart_unless_more_than_three_aired_that_day()
    {
        var start = new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc);
        var day = new DateOnly(2023, 12, 31);
        var doubleBill = Enumerable.Range(1, 2).Select(n => Ep(1, n, day, start)).ToList();
        var drop = Enumerable.Range(1, 8).Select(n => Ep(2, n, day, start)).ToList();
        var specials = Enumerable.Range(1, 5).Select(n => Ep(0, n, day, start)).ToList();
        var single = Ep(3, 1, new DateOnly(2024, 2, 1), start);

        AirTimes.SpreadSameDayEpisodes([.. doubleBill, .. drop, .. specials, single], 45);

        Assert.Equal([start, start.AddMinutes(45)], doubleBill.Select(e => e.AirDateUtc!.Value).ToArray());
        Assert.All(drop, e => Assert.Equal(start, e.AirDateUtc)); // a streaming drop stays as it is
        Assert.Equal(start.AddMinutes(4 * 45), specials[^1].AirDateUtc); // Sonarr spreads the specials whatever their count
        Assert.Equal(start, single.AirDateUtc);
    }

    [Fact]
    public void An_ended_show_without_any_date_gets_its_first_air_date_everywhere()
    {
        var episodes = new[] { Ep(1, 1, null, null), Ep(1, 2, null, null) };
        AirTimes.DateDirectToVideo(episodes, SeriesStatus.Continuing, new DateOnly(2010, 5, 1));
        Assert.All(episodes, e => Assert.Null(e.AirDateUtc));

        AirTimes.DateDirectToVideo(episodes, SeriesStatus.Ended, new DateOnly(2010, 5, 1));
        Assert.All(episodes, e => Assert.Equal(new DateTime(2010, 5, 1, 0, 0, 0, DateTimeKind.Utc), e.AirDateUtc));
        Assert.All(episodes, e => Assert.Equal(new DateOnly(2010, 5, 1), e.AirDate));
    }

    private static Episode Ep(int season, int number, DateOnly? airDate, DateTime? utc) =>
        new() { Title = $"S{season}E{number}", SeasonNumber = season, EpisodeNumber = number, AirDate = airDate, AirDateUtc = utc };
}
