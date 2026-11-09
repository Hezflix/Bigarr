// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/MultiEpisodeParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class MultiEpisodeParserFixture
    {
        [Theory]
        [InlineData("Series.S03E01-06.DUAL.BDRip.XviD.AC3.-HELLYWOOD", "Series", 3, new[] { 1, 2, 3, 4, 5, 6 })]
        [InlineData("Series.Title.103.104.720p.HDTV.X264-DIMENSION", "Series Title", 1, new[] { 3, 4 })]
        [InlineData("Series.S03E01.S03E02.720p.HDTV.X264-DIMENSION", "Series", 3, new[] { 1, 2 })]
        [InlineData("The Series S01e01 e02 ShoHD On Demand 1080i DD5 1 ALANiS", "The Series", 1, new[] { 1, 2 })]
        [InlineData("Series.Title.2x04.2x05.720p.BluRay-FUTV", "Series Title", 2, new[] { 4, 5 })]
        [InlineData("Series.Title.S07E22E23.720p.HDTV.X264-DIMENSION", "Series Title", 7, new[] { 22, 23 })]
        [InlineData("Series Title - S07E22 - S07E23 - And Lots of Security.. [HDTV-720p].mkv", "Series Title", 7, new[] { 22, 23 })]
        [InlineData("S03E01.S03E02.720p.HDTV.X264-DIMENSION", "", 3, new[] { 1, 2 })]
        [InlineData("2x04x05.720p.BluRay-FUTV", "", 2, new[] { 4, 5 })]
        [InlineData("S02E04E05.720p.BluRay-FUTV", "", 2, new[] { 4, 5 })]
        [InlineData("S02E03-04-05.720p.BluRay-FUTV", "", 2, new[] { 3, 4, 5 })]
        [InlineData("Series.Kings.S02E09-E10.HDTV.x264-ASAP", "Series Kings", 2, new[] { 9, 10 })]
        [InlineData("Series Kings - 2x9-2x10 - Served Code [SDTV] ", "Series Kings", 2, new[] { 9, 10 })]
        [InlineData("Series Kings - 2x09-2x10 - Served Code [SDTV] ", "Series Kings", 2, new[] { 9, 10 })]
        [InlineData("Hell on Series S02E09 E10 HDTV x264 EVOLVE", "Hell on Series", 2, new[] { 9, 10 })]
        [InlineData("Hell.on.Series.S02E09-E10.720p.HDTV.x264-EVOLVE", "Hell on Series", 2, new[] { 9, 10 })]
        [InlineData("Series's Sonarr - 8x01_02 - Free Falling", "Series's Sonarr", 8, new[] { 1, 2 })]
        [InlineData("8x01_02 - Free Falling", "", 8, new[] { 1, 2 })]
        [InlineData("Series.S01E91-E100", "Series", 1, new[] { 91, 92, 93, 94, 95, 96, 97, 98, 99, 100 })]
        [InlineData("Series.S29E161-E165.PDTV.x264-FQM", "Series", 29, new[] { 161, 162, 163, 164, 165 })]
        [InlineData("Shortland.Series.S22E5363-E5366.HDTV.x264-FiHTV", "Shortland Series", 22, new[] { 5363, 5364, 5365, 5366 })]
        [InlineData("the.Series.101.102.hdtv-lol", "the Series", 1, new[] { 1, 2 })]
        [InlineData("Series.10708.hdtv-lol.mp4", "Series", 1, new[] { 7, 8 })]
        [InlineData("Series.10910.hdtv-lol.mp4", "Series", 1, new[] { 9, 10 })]
        [InlineData("E.010910.HDTVx264REPACKLOL.mp4", "E", 1, new[] { 9, 10 })]
        [InlineData("World Series of Sonarr - 2010x15 - 2010x16 - HD TV.mkv", "World Series of Sonarr", 2010, new[] { 15, 16 })]
        [InlineData("The Series US S01E01-E02 720p HDTV x264", "The Series US", 1, new[] { 1, 2 })]
        [InlineData("Series Title Season 01 Episode 05-06 720p", "Series Title", 1, new[] { 5, 6 })]

        // [TestCase("My Name Is Sonarr - S03E01-E02 - My Name Is Code 28301-016 [SDTV]", "My Name Is Sonarr", 3, new[] { 1, 2 })]
        // [TestCase("Adventure Series - 5x01 - x02 - Dev the Human (2) & Sonarr the Robot (3)", "Adventure Series", 5, new [] { 1, 2 })]
        [InlineData("The Series And The Code - S42 Ep10718 - Ep10722", "The Series And The Code", 42, new[] { 10718, 10719, 10720, 10721, 10722 })]
        [InlineData("The Series And The Code - S42 Ep10688 - Ep10692", "The Series And The Code", 42, new[] { 10688, 10689, 10690, 10691, 10692 })]
        [InlineData("Series.S01E02E03.1080p.BluRay.x264-DeBTViD", "Series", 1, new[] { 2, 3 })]
        [InlineData("grp-zoos01e11e12-1080p", "grp-zoo", 1, new[] { 11, 12 })]
        [InlineData("grp-zoo-s01e11e12-1080p", "grp-zoo", 1, new[] { 11, 12 })]
        [InlineData("Series Title.S6.E1.E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6E1-E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6E1-S6E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6E1E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6E1-E2-E3.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2, 3 })]
        [InlineData("Series Title.S6.E1E3.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2, 3 })]
        [InlineData("Series Title.S6.E1-E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6.E1-S6E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6.E1E2.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2 })]
        [InlineData("Series Title.S6.E1-E2-E3.Episode Name.1080p.WEB-DL", "Series Title", 6, new[] { 1, 2, 3 })]
        [InlineData("Series.Title.S05E01-E02.720p.5.1Ch.BluRay", "Series Title", 5, new[] { 1, 2 })]
        [InlineData("Series.Title.S05E01-02.720p.5.1Ch.BluRay", "Series Title", 5, new[] { 1, 2 })]
        [InlineData("S01E01-E03 - Episode Title.HDTV-720p", "", 1, new[] { 1, 2, 3 })]
        [InlineData("1x01-x03 - Episode Title.HDTV-720p", "", 1, new[] { 1, 2, 3 })]
        [InlineData("Series.Title.E07-E08.180612.1080p-NEXT", "Series Title", 1, new[] { 7, 8 })]
        [InlineData("Series Title? E11-E12 1080p HDTV AAC H.264-NEXT", "Series Title", 1, new[] { 11, 12 })]
        [InlineData("The Series Title (2010) - [S01E01-02-03] - Episode Title", "The Series Title (2010)", 1, new[] { 1, 2, 3 })]
        [InlineData("[AqusiQ-TorrentS.pl]The.Name.of.the.Series.S01E05-06.PL.2160p-Ralf[shogho]", "The Name of the Series", 1, new[] { 5, 6 })]
        [InlineData("[AgusiQ-TorrentS.pl] The.Name.of.the.Series.S01E05-E06.PL.1080i.Ralf [jans12]", "The Name of the Series", 1, new[] { 5, 6 })]
        [InlineData("The.Name.of.the.Series.S01E05-6.PL.1080p.WEBRip.x264-666", "The Name of the Series", 1, new[] { 5, 6 })]
        [InlineData("Series Title - S15E06-07 - City Sushi HDTV-720p", "Series Title", 15, new[] { 6, 7 })]
        [InlineData("Series Title - S01E01-02-03 - Episode Title HDTV-720p", "Series Title", 1, new[] { 1, 2, 3 })]
        [InlineData("Series Title - [02x01x02] - Episode 1", "Series Title", 2, new[] { 1, 2 })]
        [InlineData("Series Title - [02x01-x02] - Episode 1", "Series Title", 2, new[] { 1, 2 })]
        [InlineData("Series Title - [02x01-02] - Episode 1", "Series Title", 2, new[] { 1, 2 })]
        [InlineData("Series Title (2011) - S01E23-E24 - ...E i nuovi orizzonti [HDTV 360p] [ITA].mkv", "Series Title (2011)", 1, new[] { 23, 24 })]
        [InlineData("The Series Title! - S01E01-02-03", "The Series Title!", 1, new[] { 1, 2, 3 })]
        [InlineData("Series Title! (2013) - S04E44-E45 - Il 200 spettacolare episodio da narcisisti!", "Series Title! (2013)", 4, new[] { 44, 45 })]
        [InlineData("Series Title! (2013) - S04E44-E45 - Il 200 spettacolare episodio da narcisisti! [NetflixHD 720p HEVC] [ITA+ENG].mkv", "Series Title! (2013)", 4, new[] { 44, 45 })]
        [InlineData("13 Series Se.1 afl.2-3-4 [VTM]", "13 Series", 1, new[] { 2, 3, 4 })]
        [InlineData("Series T Se.3 afl.3 en 4", "Series T", 3, new[] { 3, 4 })]
        [InlineData("Series Title (S15E06-08) City Sushi", "Series Title", 15, new[] { 6, 7, 8 })]
        [InlineData("Series Title (S05E06-08 of 24) City Sushi", "Series Title", 5, new[] { 6, 7, 8 })]
        [InlineData("Series Title (S05E06-08_of_24) City Sushi", "Series Title", 5, new[] { 6, 7, 8 })]
        [InlineData("Series Title (S5E6-8.of.24) City Sushi", "Series Title", 5, new[] { 6, 7, 8 })]
        [InlineData("Series Title (S5E6-8.of.XX) City Sushi", "Series Title", 5, new[] { 6, 7, 8 })]
        [InlineData("Босх: Спадок (S2E1-4) / Series: Legacy (S2E1-4) (2023) WEB-DL 1080p Ukr/Eng | sub Eng", "Series: Legacy", 2, new[] { 1, 2, 3, 4 })]
        [InlineData("Босх: Спадок / Series: Legacy / S2E1-4 of 10 (2023) WEB-DL 1080p Ukr/Eng | sub Eng", "Series: Legacy", 2, new[] { 1, 2, 3, 4 })]
        [InlineData("Series Title - S26E96-97-98-99-100 - Episode 5931 + Episode 5932 + Episode 5933 + Episode 5934 + Episode 5935", "Series Title", 26, new[] { 96, 97, 98, 99, 100 })]
        [InlineData("Series falls - Temporada 1 [HDTV][Cap.111_120]", "Series falls", 1, new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 })]
        [InlineData("Series.S101E01-E02.Some.Title.Name.1080p.WEB-DL.AAC2.0.H.264-Release", "Series", 101, new[] { 1, 2 })]
        [InlineData("Series.2006.S101E01-E02.Some.Title.Name.1080p.WEB-DL.AAC2.0.H.264-Release", "Series 2006", 101, new[] { 1, 2 })]
        [InlineData("[AL] Series.Title.S04E17-28.Part.2.German.DL.DTS.1080p.BluRay.x264-Pudding.GerSub-sama", "Series Title", 4, new[] { 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28 })]
        [InlineData("[AL] Series.Title.S04E01-16.German.DL.DTS.1080p.BluRay.x264-Pudding.GerSub-sama", "Series Title", 4, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })]

        // [TestCase("", "", , new [] {  })]
        public void should_parse_multiple_episodes(string postTitle, string title, int season, int[] episodes)
        {
            var result = Parser.ParseTitle(postTitle);
            result.SeasonNumbers.Should().Equal(season);
            result.EpisodeNumbers.Should().BeEquivalentTo(episodes);
            result.SeriesTitle.Should().Be(title);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();
        }
    }
}
