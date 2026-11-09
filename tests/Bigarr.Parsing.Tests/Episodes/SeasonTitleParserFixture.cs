// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/SeasonTitleParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests;

public class SeasonTitleParserFixture
{
    [Theory]
    [InlineData("[Group] Series Title Curse (BS11 1280x720 x264 AAC)", "Series Title Curse")]
    [InlineData("[Group] Series Title Curse (BDRip 1920x1080 x264 FLAC) [ABCD1234]", "Series Title Curse")]
    [InlineData("[Group] Series.Title - Curse (BD, 720p)", "Series.Title - Curse")]
    [InlineData("Series Title Curse [Bluray.1080p.HEVC.AAC.ITA.FLAC.JAP.Sub.Ita]", "Series Title Curse")]
    [InlineData("[Group] Series Title: Curse [Dual Audio 10bit 1080p][HEVC-x265]", "Series Title: Curse")]
    public void should_return_the_title_when_there_is_no_season_or_episode(string releaseTitle, string expectedTitle)
    {
        var result = Parser.ParseSeasonTitle(releaseTitle);

        result.Should().NotBeNull();
        result.SeriesTitle.Should().Be(expectedTitle);
        result.FullSeason.Should().BeTrue();
        result.IsSeasonTitle.Should().BeTrue();
        result.SeasonNumbers.Should().BeEmpty();
        result.EpisodeNumbers.Should().BeEmpty();
        result.AbsoluteEpisodeNumbers.Should().BeEmpty();
    }

    [Theory]
    [InlineData("[Group] Series Title Curse (BD 1280x720 x264 AAC)")]
    [InlineData("[Group] Series Title: Curse [Dual Audio 10bit 1080p][HEVC-x265]")]
    public void should_populate_the_fields_the_decision_engine_reads(string releaseTitle)
    {
        var result = Parser.ParseSeasonTitle(releaseTitle);

        result.Quality.Should().NotBeNull();
        result.Quality.Quality.Should().NotBeNull();
        result.Languages.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("[Group][Series Title: Curse][BDRip][1080P_x265(10bit)-FLAC][ALL]")]
    [InlineData("[Group][Series Title Curse][1080p][x264]")]
    [InlineData("[Group] Series Title Curse () (1080p)")]
    public void should_not_return_a_title_when_stripping_leaves_metadata(string releaseTitle)
    {
        Parser.ParseSeasonTitle(releaseTitle).Should().BeNull();
    }

    [Theory]
    [InlineData("[Group] Series Title Curse (OVA)")]
    [InlineData("[Group] Series Title Curse [OVA]")]
    [InlineData("[Group] Series Title Curse (Special)")]
    [InlineData("[Group] Series Title Curse (NCOP)")]
    [InlineData("[Group] Series Title Curse (NCED)")]
    public void should_not_return_a_title_for_an_extra(string releaseTitle)
    {
        Parser.ParseSeasonTitle(releaseTitle).Should().BeNull();
    }

    [Theory]
    [InlineData("Series Title S02E05 1080p WEB-DL")]
    [InlineData("[Group] Series Title - 05 (1080p)")]
    [InlineData("Series Title S02 1080p WEB-DL")]
    public void should_not_flag_a_release_the_regexes_can_parse(string releaseTitle)
    {
        Parser.ParseTitle(releaseTitle).IsSeasonTitle.Should().BeFalse();
    }

    [Theory]
    [InlineData("THIS SHOULD NEVER PARSE")]
    [InlineData("Vrq6e1Aba3U amCjuEgV5R2QvdsLEGYF3YQAQkw8")]
    [InlineData("[Group] Series Title Curse (BS11 1280x720 x264 AAC)")]
    public void should_leave_parse_title_rejecting_what_it_rejects_today(string releaseTitle)
    {
        Parser.ParseTitle(releaseTitle).Should().BeNull();
    }
}
