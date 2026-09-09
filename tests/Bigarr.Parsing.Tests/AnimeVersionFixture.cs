// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/AnimeVersionFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Tests
{
    public class AnimeVersionFixture
    {
        [Theory]
        [InlineData("Anime Title - 2018 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 1)]
        [InlineData("Anime Title - 2018v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
        [InlineData("Anime Title - 2018 v2 - (BD 1080p HEVC FLAC) [Dual Audio] [Group]", 2)]
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
