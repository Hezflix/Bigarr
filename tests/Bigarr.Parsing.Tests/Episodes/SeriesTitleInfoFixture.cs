// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/SeriesTitleInfoFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class SeriesTitleInfoFixture
    {
        [Fact]
        public void should_have_year_zero_when_title_doesnt_have_a_year()
        {
            const string title = "Series.Title.S01E01.pilot.720p.hdtv";

            var result = Parser.ParseTitle(title).SeriesTitleInfo;

            result.Year.Should().Be(0);
        }

        [Fact]
        public void should_have_same_title_for_title_and_title_without_year_when_title_doesnt_have_a_year()
        {
            const string title = "Series.Title.S01E01.pilot.720p.hdtv";

            var result = Parser.ParseTitle(title).SeriesTitleInfo;

            result.Title.Should().Be(result.TitleWithoutYear);
        }

        [Fact]
        public void should_have_year_when_title_has_a_year()
        {
            const string title = "Series.Title.2004.S01E01.pilot.720p.hdtv";

            var result = Parser.ParseTitle(title).SeriesTitleInfo;

            result.Year.Should().Be(2004);
        }

        [Fact]
        public void should_have_year_in_title_when_title_has_a_year()
        {
            const string title = "Series.Title.2004.S01E01.pilot.720p.hdtv";

            var result = Parser.ParseTitle(title).SeriesTitleInfo;

            result.Title.Should().Be("Series Title 2004");
        }

        [Fact]
        public void should_title_without_year_should_not_contain_year()
        {
            const string title = "Series.Title.2004.S01E01.pilot.720p.hdtv";

            var result = Parser.ParseTitle(title).SeriesTitleInfo;

            result.TitleWithoutYear.Should().Be("Series Title");
        }
    }
}
