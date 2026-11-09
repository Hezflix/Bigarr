// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/ParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Qualities;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class ParserFixture
    {
        [Theory]
        [InlineData("Series Title - 4x05 - Title", "seriestitle")]
        [InlineData("Series & Title - 4x05 - Title", "seriestitle")]
        [InlineData("Bad Format", "badformat")]
        [InlineData("Mad Series - Season 1 [Bluray720p]", "madseries")]
        [InlineData("Mad Series - Season 1 [Bluray1080p]", "madseries")]
        [InlineData("The Daily Series -", "thedailyseries")]
        [InlineData("The Series Bros. (2006)", "theseriesbros2006")]
        [InlineData("Series (2011)", "series2011")]
        [InlineData("Series Time S02 720p HDTV x264 CRON", "seriestime")]
        [InlineData("Series Title 0", "seriestitle0")]
        [InlineData("Series of the Day", "seriesday")]
        [InlineData("Series of the Day 2", "seriesday2")]
        [InlineData("[ www.Torrenting.com ] - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
        [InlineData("www.Torrenting.com - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
        [InlineData("Series S02E09 HDTV x264-2HD [eztv]-[rarbg.com]", "series")]
        [InlineData("Series.911.S01.DVDRip.DD2.0.x264-DEEP", "series 911")]
        [InlineData("www.Torrenting.org - Series.S03E14.720p.HDTV.X264-DIMENSION", "series")]
        public void should_parse_series_name(string postTitle, string title)
        {
            var result = Parser.ParseSeriesName(postTitle).CleanSeriesTitle();
            result.Should().Be(title.CleanSeriesTitle());
        }

        [Theory]
        [InlineData("Series S03E14 720p HDTV X264-DIMENSION", "Series")]
        [InlineData("Series.S03E14.720p.HDTV.X264-DIMENSION", "Series")]
        [InlineData("Series-S03E14-720p-HDTV-X264-DIMENSION", "Series")]
        [InlineData("Series_S03E14_720p_HDTV_X264-DIMENSION", "Series")]
        [InlineData("Series 2022 S03E14 720p HDTV X264-DIMENSION", "Series", 2022)]
        [InlineData("Series (2022) S03E14 720p HDTV X264-DIMENSION", "Series", 2022)]
        [InlineData("Series.2022.S03E14.720p.HDTV.X264-DIMENSION", "Series", 2022)]
        [InlineData("Series-2022-S03E14-720p-HDTV-X264-DIMENSION", "Series", 2022)]
        [InlineData("Series_2022_S03E14_720p_HDTV_X264-DIMENSION", "Series", 2022)]
        [InlineData("1234 S03E14 720p HDTV X264-DIMENSION", "1234")]
        [InlineData("1234.S03E14.720p.HDTV.X264-DIMENSION", "1234")]
        [InlineData("1234-S03E14-720p-HDTV-X264-DIMENSION", "1234")]
        [InlineData("1234_S03E14_720p_HDTV_X264-DIMENSION", "1234")]
        [InlineData("1234 2022 S03E14 720p HDTV X264-DIMENSION", "1234", 2022)]
        [InlineData("1234 (2022) S03E14 720p HDTV X264-DIMENSION", "1234", 2022)]
        [InlineData("1234.2022.S03E14.720p.HDTV.X264-DIMENSION", "1234", 2022)]
        [InlineData("1234-2022-S03E14-720p-HDTV-X264-DIMENSION", "1234", 2022)]
        [InlineData("1234_2022_S03E14_720p_HDTV_X264-DIMENSION", "1234", 2022)]
        public void should_parse_series_title_info(string postTitle, string titleWithoutYear, int year = 0)
        {
            var seriesTitleInfo = Parser.ParseTitle(postTitle).SeriesTitleInfo;
            seriesTitleInfo.TitleWithoutYear.Should().Be(titleWithoutYear);
            seriesTitleInfo.Year.Should().Be(year);
        }

        [Fact]
        public void should_remove_accents_from_title()
        {
            const string title = "Seri\u00E0es";

            title.CleanSeriesTitle().Should().Be("seriaes");
        }

        [Theory]
        [InlineData("Sonar TV - Series Title : 02 Road From Code [S04].mp4")]
        public void should_clean_up_invalid_path_characters(string postTitle)
        {
            Parser.ParseTitle(postTitle);
        }

        [Theory]
        [InlineData("[scnzbefnet][509103] 2.Developers.Series.S03E18.720p.HDTV.X264-DIMENSION", "2 Developers Series")]
        public void should_remove_request_info_from_title(string postTitle, string title)
        {
            Parser.ParseTitle(postTitle).SeriesTitle.Should().Be(title);
        }

        [Theory]
        [InlineData("Series.S01E02.Chained.Title.mkv")]
        [InlineData("Show - S01E01 - Title.avi")]
        public void should_parse_quality_from_extension(string title)
        {
            Parser.ParseTitle(title).Quality.Quality.Should().NotBe(Quality.Unknown);
            Parser.ParseTitle(title).Quality.SourceDetectionSource.Should().Be(QualityDetectionSource.Extension);
            Parser.ParseTitle(title).Quality.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Theory]
        [InlineData("Series.S01E02.Chained.Title.mkv", "Series.S01E02.Chained.Title")]
        public void should_parse_releasetitle(string path, string releaseTitle)
        {
            var result = Parser.ParseTitle(path);
            result.ReleaseTitle.Should().Be(releaseTitle);
        }

        [Theory]
        [InlineData("Босх: Спадок (S2E1) / Series: Legacy (S2E1) (2023) WEB-DL 1080p Ukr/Eng | sub Eng", "Босх: Спадок", "Series: Legacy")]
        [InlineData("Босх: Спадок / Series: Legacy / S2E1-4 of 10 (2023) WEB-DL 1080p Ukr/Eng | sub Eng", "Босх: Спадок", "Series: Legacy")]
        [InlineData("Босх: Спадок AKA Series: Legacy S02 1080p NF WEB-DL Dual- Audio DD+ 5.1 Atmos H.264-APEX", "Босх: Спадок", "Series: Legacy")]
        [InlineData("Босх.Спадок.AKA.Series.Legacy.S02.1080p.NF.WEB-DL.DUAL.DDP5.1.Atmos.H.264-APEX", "Босх Спадок", "Series Legacy")]
        [InlineData("[Judas] Series E (Series J) (Season 04) [1080p][HEVC x265 10bit][Dual-Audio][Multi-Subs] (Batch)", "Series E", "Series J")]
        [InlineData("Serie / Series [S01E02] [MULTI] [1080p] [HMAX] [WEB-DL] [H.264] [DDP5.1.Atmos-K83] [Lektor i Napisy PL]", "Serie", "Series")]
        public void should_parse_multiple_series_titles(string postTitle, params string[] titles)
        {
            var seriesTitleInfo = Parser.ParseTitle(postTitle).SeriesTitleInfo;
            seriesTitleInfo.AllTitles.Should().BeEquivalentTo(titles);
        }

        [Theory]
        [InlineData("[Reza] Series in Russian - S01E08 [WEBRip 1080p HEVC AAC] (Dual Audio) (Tokidoki Bosotto Russiago de Dereru Tonari no Alya-san)", "Unknown")]
        public void should_parse_language_after_parsing_title(string postTitle, string expectedLanguage)
        {
            var result = Parser.ParseTitle(postTitle);

            result.Languages.Count.Should().Be(1);
            result.Languages.Should().Contain((Language)expectedLanguage);
        }
    }
}
