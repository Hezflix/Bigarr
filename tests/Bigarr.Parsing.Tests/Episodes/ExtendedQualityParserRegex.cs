// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/ExtendedQualityParserRegex.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class ExtendedQualityParserRegex
    {
        [Theory]
        [InlineData("Series.Title.S04E05.HDTV.XviD-LOL", 0)]
        [InlineData("Series.Title.S04E05.Garnets.or.Gold.REAL.REAL.PROPER.HDTV.x264-W4F", 2)]
        [InlineData("Series.Title.S03E17.REAL.PROPER.720p.HDTV.x264-ORENJI-RP", 1)]
        [InlineData("Series.Title.S05E09.REAL.PROPER.HDTV.x264-KILLERS", 1)]
        [InlineData("Series.Title.S14E01.REAL.PROPER.720p.HDTV.x264-KILLERS", 1)]
        [InlineData("Series.Title.s02e06.real.proper.720p.webrip.x264-2hd", 0)]
        [InlineData("Series.Title.S21E07.Super.Duper.Real.Proper.HDTV.x264-FTP", 0)]
        [InlineData("Series.Title.S21E07.PROPER.HDTV.x264-RiVER-RP", 0)]
        [InlineData("Series.Title.S07E11.PROPER.REAL.RERIP.1080p.BluRay.x264-TENEIGHTY", 1)]
        [InlineData("[MGS] - Series Title - Episode 02v2 - [D8B6C90D]", 0)]
        [InlineData("[Hatsuyuki] Series Title - 07 [v2][848x480][23D8F455].avi", 0)]
        [InlineData("[DeadFish] Series Title - 01v3 [720p][AAC]", 0)]
        [InlineData("[DeadFish] Series Title - 01v4 [720p][AAC]", 0)]
        [InlineData("The Real Housewives of Some Place - S01E01 - Why are we doing this?", 0)]
        public void should_parse_reality_from_title(string title, int reality)
        {
            QualityParser.ParseQuality(title).Revision.Real.Should().Be(reality);
        }

        [Theory]
        [InlineData("Series.Title.S04E05.HDTV.XviD-LOL", 1)]
        [InlineData("Series.Title.S04E05.Garnets.or.Gold.REAL.REAL.PROPER.HDTV.x264-W4F", 2)]
        [InlineData("Series.Title.S03E17.REAL.PROPER.720p.HDTV.x264-ORENJI-RP", 2)]
        [InlineData("Series.Title.S05E09.REAL.PROPER.HDTV.x264-KILLERS", 2)]
        [InlineData("Series.Title.S14E01.REAL.PROPER.720p.HDTV.x264-KILLERS", 2)]
        [InlineData("Series.Title.Is.the.New.Black.s02e06.real.proper.720p.webrip.x264-2hd", 2)]
        [InlineData("Series.Title.S21E07.Super.Duper.Real.Proper.HDTV.x264-FTP", 2)]
        [InlineData("Series.Title.S21E07.PROPER.HDTV.x264-RiVER-RP", 2)]
        [InlineData("Series.Title.S07E11.PROPER.REAL.RERIP.1080p.BluRay.x264-TENEIGHTY", 2)]
        [InlineData("[MGS] - Series Title - Episode 02v2 - [D8B6C90D]", 2)]
        [InlineData("[Hatsuyuki] Series Title - 07 [v2][848x480][23D8F455].avi", 2)]
        [InlineData("[DeadFish] Series Title - 01v3 [720p][AAC]", 3)]
        [InlineData("[DeadFish]Series Title - 01v4 [720p][AAC]", 4)]
        [InlineData("[Vivid-Asenshi] Series Title - 04v2 [266EE983]", 2)]
        [InlineData("[Vivid-Asenshi] Series Title - 03v2 [66A05817]", 2)]
        [InlineData("[Vivid-Asenshi] Series Title - 02v2 [1F67AB55]", 2)]
        public void should_parse_version_from_title(string title, int version)
        {
            QualityParser.ParseQuality(title).Revision.Version.Should().Be(version);
        }
    }
}
