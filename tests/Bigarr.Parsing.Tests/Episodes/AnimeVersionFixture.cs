// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/AnimeVersionFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class AnimeVersionFixture
    {
        [Theory]
        [InlineData("Anime Title - S01E01 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 1)]
        [InlineData("Anime Title - S01E01v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
        [InlineData("Anime Title - S01E01 v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
        [InlineData("[SubsPlease] Anime Title - 01 (1080p) [B1F227CF]", 1)]
        [InlineData("[SubsPlease] Anime Title - 01v2 (1080p) [B1F227CF]", 2)]
        [InlineData("[SubsPlease] Anime Title - 01 v2 (1080p) [B1F227CF]", 2)]
        public void should_be_able_to_parse_repack(string title, int version)
        {
            var result = QualityParser.ParseQuality(title);
            result.Revision.Version.Should().Be(version);
        }
    }
}
