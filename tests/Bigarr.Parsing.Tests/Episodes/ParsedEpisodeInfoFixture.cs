// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/ParsedEpisodeInfoFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using Bigarr.Parsing.Episodes.Model;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class ParsedEpisodeInfoFixture
    {
        [Fact]
        public void should_be_possible_scene_season_special_for_e00_in_a_regular_season()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeasonNumbers = [2], EpisodeNumbers = [0] };

            parsedEpisodeInfo.IsPossibleSceneSeasonSpecial.Should().BeTrue();
        }

        [Fact]
        public void should_not_be_possible_scene_season_special_in_season_zero()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeasonNumbers = [0], EpisodeNumbers = [0] };

            parsedEpisodeInfo.IsPossibleSceneSeasonSpecial.Should().BeFalse();
        }

        [Fact]
        public void should_not_be_possible_scene_season_special_without_a_season()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeasonNumbers = [], EpisodeNumbers = [0] };

            parsedEpisodeInfo.IsPossibleSceneSeasonSpecial.Should().BeFalse();
        }

        [Fact]
        public void should_not_be_possible_special_episode_without_a_season_or_series_title()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeriesTitle = "", SeasonNumbers = [], EpisodeNumbers = [7] };

            parsedEpisodeInfo.IsPossibleSpecialEpisode.Should().BeFalse();
        }

        [Fact]
        public void should_be_possible_special_episode_in_season_zero_without_series_title()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeriesTitle = "", SeasonNumbers = [0], EpisodeNumbers = [7] };

            parsedEpisodeInfo.IsPossibleSpecialEpisode.Should().BeTrue();
        }

        [Theory]
        [InlineData(new[] { 1, 2, 3 }, "Seasons 01-03")]
        [InlineData(new[] { 1, 3, 5 }, "Seasons 01, 03, 05")]
        [InlineData(new[] { 2 }, "Season 02")]
        [InlineData(new int[0], "[Unknown Season]")]
        public void should_format_full_season(int[] seasonNumbers, string expected)
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeriesTitle = "Series Title", SeasonNumbers = [.. seasonNumbers], FullSeason = true };

            parsedEpisodeInfo.ToString().Should().Contain(expected);
        }

        [Theory]
        [InlineData("Series.Title.S01-S03.1080p.BluRay.x264-RlsGrp", ReleaseType.MultiSeasonPack)]
        [InlineData("Series.Title.S01.1080p.BluRay.x264-RlsGrp", ReleaseType.SeasonPack)]
        [InlineData("Series.Title.S01E01-E02.1080p.BluRay.x264-RlsGrp", ReleaseType.MultiEpisode)]
        [InlineData("Series.Title.S01E01.1080p.BluRay.x264-RlsGrp", ReleaseType.SingleEpisode)]
        public void should_get_release_type(string title, ReleaseType expected)
        {
            Parser.ParseTitle(title).ReleaseType.Should().Be(expected);
        }

        [Fact]
        public void should_format_episode_with_first_season()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo { SeriesTitle = "Series Title", SeasonNumbers = [2], EpisodeNumbers = [5, 6] };

            parsedEpisodeInfo.ToString().Should().Contain("S02E05-06");
        }
    }
}
