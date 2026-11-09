// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/AnimeMetadataParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class AnimeMetadataParserFixture
    {
        [Theory]
        [InlineData("[SubDESU]_Show_Title_DxD_07_(1280x720_x264-AAC)_[6B7FD717]", "SubDESU", "6B7FD717")]
        [InlineData("[Chihiro]_Show_Title!!_-_06_[848x480_H.264_AAC][859EEAFA]", "Chihiro", "859EEAFA")]
        [InlineData("[Underwater]_Show_Title_-_12_(720p)_[5C7BC4F9]", "Underwater", "5C7BC4F9")]
        [InlineData("[HorribleSubs]_Show_Title_-_33_[720p]", "HorribleSubs", "")]
        [InlineData("[HorribleSubs] Show-Title - 13 [1080p].mkv", "HorribleSubs", "")]
        [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].[C65D4B1F].mkv", "Doremi", "C65D4B1F")]
        [InlineData("[Doremi].Show.Title.5.Go.Go!.31[1280x720].[C65D4B1F]", "Doremi", "C65D4B1F")]
        [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].mkv", "Doremi", "")]
        [InlineData("[K-F] Series Title 214", "K-F", "")]
        [InlineData("[K-F] Series Title S10E14 214", "K-F", "")]
        [InlineData("[K-F] Series Title 10x14 214", "K-F", "")]
        [InlineData("[K-F] Series Title 214 10x14", "K-F", "")]
        [InlineData("Series Title - 031 - The Resolution to Kill [Lunar].avi", "Lunar", "")]
        [InlineData("[ACX]Series Title 01 Episode Name [Kosaka] [9C57891E].mkv", "ACX", "9C57891E")]
        [InlineData("[S-T-D] Series Title! - 06 (1280x720 10bit AAC) [59B3F2EA].mkv", "S-T-D", "59B3F2EA")]

        // These tests are dupes of the above, except with parenthesized hashes instead of square bracket
        [InlineData("[SubDESU]_Show_Title_DxD_07_(1280x720_x264-AAC)_(6B7FD717)", "SubDESU", "6B7FD717")]
        [InlineData("[Chihiro]_Show_Title!!_-_06_[848x480_H.264_AAC](859EEAFA)", "Chihiro", "859EEAFA")]
        [InlineData("[Underwater]_Show_Title_-_12_(720p)_(5C7BC4F9)", "Underwater", "5C7BC4F9")]
        [InlineData("[Doremi].Show.Title.5.Go.Go!.31.[1280x720].(C65D4B1F).mkv", "Doremi", "C65D4B1F")]
        [InlineData("[Doremi].Show.Title.5.Go.Go!.31[1280x720].(C65D4B1F)", "Doremi", "C65D4B1F")]
        [InlineData("[ACX]Series Title 01 Episode Name [Kosaka] (9C57891E).mkv", "ACX", "9C57891E")]
        [InlineData("[S-T-D] Series Title! - 06 (1280x720 10bit AAC) (59B3F2EA).mkv", "S-T-D", "59B3F2EA")]
        public void should_parse_releasegroup_and_hash(string postTitle, string subGroup, string hash)
        {
            var result = Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.ReleaseGroup.Should().Be(subGroup);
            result.ReleaseHash.Should().Be(hash);
        }

        [Theory]
        [InlineData("[DHD] Series Title! - 08 (1280x720 10bit AAC) [8B00F2EA].mkv", "8B00F2EA")]
        [InlineData("[DHD] Series Title! - 10 (1280x720 10bit AAC) [10BBF2EA].mkv", "10BBF2EA")]
        [InlineData("[DHD] Series Title! - 08 (1280x720 10bit AAC) [008BF28B].mkv", "008BF28B")]
        [InlineData("[DHD] Series Title! - 10 (1280x720 10bit AAC) [000BF10B].mkv", "000BF10B")]
        [InlineData("[DHD] Series Title! - 08 (1280x720 8bit AAC) [8B8BF2EA].mkv", "8B8BF2EA")]
        [InlineData("[DHD] Series Title! - 10 (1280x720 8bit AAC) [10B10BEA].mkv", "10B10BEA")]
        public void should_parse_release_hashes_with_10b_or_8b(string postTitle, string hash)
        {
            var result = Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.ReleaseHash.Should().Be(hash);
        }
    }
}
