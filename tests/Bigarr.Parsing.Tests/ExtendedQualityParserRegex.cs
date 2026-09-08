// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/ExtendedQualityParserRegex.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Tests
{
    public class ExtendedQualityParserRegex
    {
        [Theory]
        [InlineData("Movie.Title.HDTV.XviD-LOL", 0)]
        [InlineData("Movie.Title.Garnets.or.Gold.REAL.REAL.PROPER.HDTV.x264-W4F", 2)]
        [InlineData("Movie.Title.REAL.PROPER.720p.HDTV.x264-ORENJI-RP", 1)]
        [InlineData("Movie.Title.REAL.PROPER.HDTV.x264-KILLERS", 1)]
        [InlineData("Movie.Title.REAL.PROPER.720p.HDTV.x264-KILLERS", 1)]
        [InlineData("Movie.Title.New.Black.real.proper.720p.webrip.x264-2hd", 0)]
        [InlineData("Movie.Title.Super.Duper.Real.Proper.HDTV.x264-FTP", 0)]
        [InlineData("Movie.Title.PROPER.HDTV.x264-RiVER-RP", 0)]
        [InlineData("Movie.Title.PROPER.REAL.RERIP.1080p.BluRay.x264-TENEIGHTY", 1)]
        [InlineData("[MGS] - Movie.Title - Episode 02v2 - [D8B6C90D]", 0)]
        [InlineData("[Hatsuyuki] Movie Title - 07 [v2][848x480][23D8F455].avi", 0)]
        [InlineData("[DeadFish] Movie Title - 01v3 [720p][AAC]", 0)]
        [InlineData("[DeadFish] Movie Title Sword - 01v4 [720p][AAC]", 0)]
        [InlineData("The Real Movie.Titlewives of Some Place - S01E01 - Why are we doing this?", 0)]
        public void should_parse_reality_from_title(string title, int reality)
        {
            QualityParser.ParseQuality(title).Revision.Real.Should().Be(reality);
        }

        [Theory]
        [InlineData("Movie.Title.HDTV.XviD-LOL", 1)]
        [InlineData("Movie.Title.Garnets.or.Gold.REAL.REAL.PROPER.HDTV.x264-W4F", 2)]
        [InlineData("Movie.Title.REAL.PROPER.720p.HDTV.x264-ORENJI-RP", 2)]
        [InlineData("Movie.Title.REAL.PROPER.HDTV.x264-KILLERS", 2)]
        [InlineData("Movie.Title.REAL.PROPER.720p.HDTV.x264-KILLERS", 2)]
        [InlineData("Movie.Title.New.Black.real.proper.720p.webrip.x264-2hd", 2)]
        [InlineData("Movie.Title.Super.Duper.Real.Proper.HDTV.x264-FTP", 2)]
        [InlineData("Movie.Title.PROPER.HDTV.x264-RiVER-RP", 2)]
        [InlineData("Movie.Title.PROPER.REAL.RERIP.1080p.BluRay.x264-TENEIGHTY", 2)]
        public void should_parse_version_from_title(string title, int version)
        {
            QualityParser.ParseQuality(title).Revision.Version.Should().Be(version);
        }

        [Theory]
        [InlineData("Movie 2016 2160p 4K UltraHD BluRay DTS-HD MA 7 1 x264-Whatevs", 19)]
        [InlineData("Movie 2016 2160p 4K UltraHD DTS-HD MA 7 1 x264-Whatevs", 16)]
        [InlineData("Movie 2016 4K 2160p UltraHD BluRay AAC2 0 HEVC x265", 19)]
        [InlineData("The Movie 2015 2160p UHD BluRay DTS x264-Whatevs", 19)]
        [InlineData("The Movie 2015 2160p UHD BluRay FLAC 7 1 x264-Whatevs", 19)]
        [InlineData("The Movie 2015 2160p Ultra HD BluRay DTS-HD MA 7 1 x264-Whatevs", 19)]
        [InlineData("Into the Movie 2016 2160p Netflix WEBRip DD5 1 x264-Whatevs", 18)]
        public void should_parse_ultrahd_from_title(string title, int version)
        {
            var parsed = QualityParser.ParseQuality(title);
            parsed.Quality.Resolution.Should().Be((int)Resolution.R2160p);
        }
    }
}
