// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/SeasonNumbersFormatterFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class SeasonNumbersFormatterFixture
    {
        [Theory]
        [InlineData(new[] { 2 }, "Season 2")]
        [InlineData(new[] { 2, 2 }, "Season 2")]
        [InlineData(new[] { 1, 2, 3 }, "Seasons 1-3")]
        [InlineData(new[] { 3, 1, 2 }, "Seasons 1-3")]
        [InlineData(new[] { 1, 3, 5 }, "Seasons 1, 3, 5")]
        public void should_format_season_numbers(int[] seasonNumbers, string expected)
        {
            SeasonNumbersFormatter.Format(seasonNumbers).Should().Be(expected);
        }

        [Theory]
        [InlineData(new[] { 2 }, "Season 02")]
        [InlineData(new[] { 1, 2 }, "Seasons 01-02")]
        [InlineData(new[] { 1, 3 }, "Seasons 01, 03")]
        public void should_format_season_numbers_with_number_format(int[] seasonNumbers, string expected)
        {
            SeasonNumbersFormatter.Format(seasonNumbers, "00").Should().Be(expected);
        }
    }
}
