// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/MiniSeriesEpisodeParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class MiniSeriesEpisodeParserFixture
    {
        [Theory]
        [InlineData("The.Big.Series.Leader.Part.2.DSR.XviD-SYS", "The Big Series Leader", 2)]
        [InlineData("kill-roy-was-here-e07-720p", "kill-roy-was-here", 7)]
        [InlineData("Series and Show 2012 Part 1 REPACK 720p HDTV x264 2HD", "Series and Show 2012", 1)]
        [InlineData("Series Show.2016.E04.Power.720p.WEB-DL.DD5.1.H.264-MARS", "Series Show 2016", 4)]

        // [TestCase("Killroy.Jumped.And.Was.Here.EP02.Episode.Title.DVDRiP.XviD-DEiTY", "Killroy.Jumped.And.Was.Here", 2)]
        // [TestCase("", "", 0)]
        public void should_parse_mini_series_episode(string postTitle, string title, int episodeNumber)
        {
            var result = Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.EpisodeNumbers.Should().HaveCount(1);
            result.SeasonNumbers.Should().Equal(1);
            result.EpisodeNumbers.First().Should().Be(episodeNumber);
            result.SeriesTitle.Should().Be(title);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();
        }

        [Theory]
        [InlineData("It's a Series Title.E56.190121.720p-NEXT.mp4", "It's a Series Title", 56, "2019-01-21")]
        [InlineData("My Only Series Title.E37.190120.1080p-NEXT.mp4", "My Only Series Title", 37, "2019-01-20")]
        [InlineData("Series.E191.190121.720p-NEXT.mp4", "Series", 191, "2019-01-21")]
        [InlineData("The Series Title Challenge.E932.190120.720p-NEXT.mp4", "The Series Title Challenge", 932, "2019-01-20")]
        [InlineData("백번의 추억.E12.251019.1080p-F1RST", "백번의 추억", 12, "2025-01-19")]
        [InlineData("나는 SOLO <나는 솔로>.E223.251015.1080p-F1RST", "나는 SOLO 나는 솔로", 223, "2025-10-15")]
        [InlineData("나는 SOLO 그 후, 사랑은 계속된다(나솔사계).E135.251016.1080p-F1RST", "나는 SOLO 그 후, 사랑은 계속된다(나솔사계)", 135, "2025-10-16")]

        // [TestCase("", "", 0, "")]
        public void should_parse_korean_series_episode(string postTitle, string title, int episodeNumber, string airdate)
        {
            var result = Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.EpisodeNumbers.Should().HaveCount(1);
            result.SeasonNumbers.Should().Equal(1);
            result.EpisodeNumbers.First().Should().Be(episodeNumber);
            result.SeriesTitle.Should().Be(title);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();

            // We don't support both SxxExx and airdate yet
            // result.AirDate.Should().Be(airdate);
        }
    }
}
