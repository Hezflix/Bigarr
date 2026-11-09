// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/NormalizeEpisodeTitleFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class NormalizeEpisodeTitleFixture
    {
        [Theory]
        [InlineData("Episode Title", "episode title")]
        [InlineData("A.B,C;", "a b c")]
        [InlineData("Episode  Title", "episode title")]
        [InlineData("French Title (1)", "french title")]
        [InlineData("Series.Title.S01.Special.Episode.Title.720p.HDTV.x264-Sonarr", "episode title")]
        [InlineData("Series.Title.S01E00.Episode.Title.720p.HDTV.x264-Sonarr", "episode title")]
        public void should_normalize_episode_title(string input, string expected)
        {
            var result = Parser.NormalizeEpisodeTitle(input);

            result.Should().Be(expected);
        }
    }
}
