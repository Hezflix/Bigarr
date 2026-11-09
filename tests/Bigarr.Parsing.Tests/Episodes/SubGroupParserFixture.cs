// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/SubGroupParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class SubGroupParserFixture
    {
        [Theory]
        [InlineData("[GHOST][1080p] Series - 25 [BD HEVC 10bit Dual Audio AC3][AE0ADDBA]", "GHOST")]
        public void should_parse_sub_group_from_title_as_release_group(string title, string expected)
        {
            var result = Parser.ParseTitle(title);

            result.Should().NotBeNull();
            result.ReleaseGroup.Should().Be(expected);
        }
    }
}
