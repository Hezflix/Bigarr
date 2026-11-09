// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/UrlFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class UrlFixture
    {
        [Theory]
        [InlineData("[www.test.com] - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("test.net - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("[www.test-hyphen.com] - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("www.test123.org - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("[test.co.uk] - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("www.test-hyphen.net.au - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("[www.test123.co.nz] - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("test-hyphen123.org.au - Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("[www.test123.de] - Mad Series - Season 1 [Bluray720p]", "Mad Series")]
        [InlineData("www.test-hyphen.de - Mad Series - Season 1 [Bluray1080p]", "Mad Series")]
        [InlineData("[test-hyphen123.co.za] - The Daily Series - 2023-05-26", "The Daily Series")]
        [InlineData("www.test123.co.za - The Series Bros. (2006) - S01E01", "The Series Bros. (2006)")]
        [InlineData("[www.test-hyphen.ca] - Series (2011) S01", "Series (2011)")]
        [InlineData("test123.ca - Series Time S02 720p HDTV x264 CRON", "Series Time")]
        [InlineData("[www.test-hyphen123.co.za] - Series Title S01E01", "Series Title")]
        [InlineData("(seriesawake.com) Series Super - 57 [720p] [English Subbed]", "Series Super")]

        public void should_not_parse_url_in_name(string postTitle, string title)
        {
            var result = Parser.ParseSeriesName(postTitle).CleanSeriesTitle();
            result.Should().Be(title.CleanSeriesTitle());
        }

        [Theory]
        [InlineData("Series.2009.S01E14.English.HDTV.XviD-LOL[www.abb.com]", "LOL")]
        [InlineData("Series 2009 S01E14 English HDTV XviD LOL[www.academy.org]", null)]
        [InlineData("Series Now S05 EXTRAS DVDRip XviD RUNNER[www.aetna.net]", null)]
        [InlineData("Series.Title.S01.EXTRAS.DVDRip.XviD-RUNNER[www.alfaromeo.io]", "RUNNER")]
        [InlineData("2020.Series.2011.12.02.PDTV.XviD-C4TV[rarbg.to]", "C4TV")]
        [InlineData("Series.Title.S01E14.English.HDTV.XviD-LOL[www.abbott.gov]", "LOL")]
        [InlineData("Series 2020 S01E14 English HDTV XviD LOL[www.actor.org]", null)]
        [InlineData("Series Live S05 EXTRAS DVDRip XviD RUNNER[www.agency.net]", null)]
        [InlineData("Series.Title.S02.EXTRAS.DVDRip.XviD-RUNNER[www.airbus.io]", "RUNNER")]
        [InlineData("2021.Series.2012.12.02.PDTV.XviD-C4TV[rarbg.to]", "C4TV")]
        [InlineData("Series.2020.S01E14.English.HDTV.XviD-LOL[www.afl.com]", "LOL")]
        [InlineData("Series 2021 S01E14 English HDTV XviD LOL[www.adult.org]", null)]
        [InlineData("Series Future S05 EXTRAS DVDRip XviD RUNNER[www.allstate.net]", null)]
        [InlineData("Series.Title.S03.EXTRAS.DVDRip.XviD-RUNNER[www.ally.io]", "RUNNER")]
        [InlineData("2022.Series.2013.12.02.PDTV.XviD-C4TV[rarbg.to]", "C4TV")]

        public void should_not_parse_url_in_group(string title, string expected)
        {
            ReleaseGroupParser.ParseReleaseGroup(title).Should().Be(expected);
        }
    }
}
