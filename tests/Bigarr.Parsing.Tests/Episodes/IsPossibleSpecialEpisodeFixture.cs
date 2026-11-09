// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/IsPossibleSpecialEpisodeFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using Bigarr.Parsing.Episodes.Model;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class IsPossibleSpecialEpisodeFixture
    {
        [Fact]
        public void should_not_treat_files_without_a_series_title_as_a_special()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo
                                    {
                                        EpisodeNumbers = [7],
                                        SeasonNumbers = [1],
                                        SeriesTitle = ""
                                    };

            parsedEpisodeInfo.IsPossibleSpecialEpisode.Should().BeFalse();
        }

        [Fact]
        public void should_return_true_when_episode_numbers_is_empty()
        {
            var parsedEpisodeInfo = new ParsedEpisodeInfo
            {
                SeasonNumbers = [1],
                SeriesTitle = ""
            };

            parsedEpisodeInfo.IsPossibleSpecialEpisode.Should().BeTrue();
        }

        [Theory]
        [InlineData("Title.the.Series.S02.Special-Inside.Chesters.Mill.HDTV.x264-BAJSKORV")]
        [InlineData("Title.the.Series.S02.Special-Inside.Chesters.Mill.720p.HDTV.x264-BAJSKORV")]
        [InlineData("Title.the.Series.S05.Special.HDTV.x264-2HD")]
        public void IsPossibleSpecialEpisode_should_be_true(string title)
        {
            Parser.ParseTitle(title).IsPossibleSpecialEpisode.Should().BeTrue();
        }

        [Theory]
        [InlineData("Title.the.Series.S11E00.A.Christmas.Carol.Special.720p.HDTV-FieldOfView")]
        public void IsPossibleSpecialEpisode_should_be_true_if_e00_special(string title)
        {
            Parser.ParseTitle(title).IsPossibleSpecialEpisode.Should().BeTrue();
        }

        [Theory]
        [InlineData("Big.Special.Show.S05.HDTV.x264-2HD")]
        public void IsPossibleSpecialEpisode_should_be_false_for_Special_in_title(string title)
        {
            Parser.ParseTitle(title).IsPossibleSpecialEpisode.Should().BeFalse();
        }
    }
}
