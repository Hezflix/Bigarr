// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/UrlFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;

namespace Bigarr.Parsing.Tests
{
    public class UrlFixture
    {
        [Theory]
        [InlineData("[www.test.com] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("test.net - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("[www.test-hyphen.com] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("www.test123.org - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("[test.co.uk] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("www.test-hyphen.net.au - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("[www.test123.co.nz] - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("test-hyphen123.org.au - Movie.Title.2023.720p.HDTV.X264-DIMENSION", "Movie Title")]
        [InlineData("[www.test123.de] - Mad Movie Title 2023 [Bluray720p]", "Mad Movie Title")]
        [InlineData("www.test-hyphen.de - Mad Movie Title 2023 [Bluray1080p]", "Mad Movie Title")]
        [InlineData("www.test123.co.za - The Movie Title Bros. (2023)", "The Movie Title Bros.")]
        [InlineData("[www.test-hyphen.ca] - Movie Title (2023)", "Movie Title")]
        [InlineData("test123.ca - Movie Time 2023 720p HDTV x264 CRON", "Movie Time")]
        [InlineData("[www.test-hyphen123.co.za] - Movie Title 2023", "Movie Title")]
        [InlineData("(movieawake.com) Movie Title 2023 [720p] [English Subbed]", "Movie Title")]
        public void should_not_parse_url_in_name(string postTitle, string title)
        {
            var result = Parser.ParseMovieTitle(postTitle).MovieTitle.CleanMovieTitle();
            result.Should().Be(title.CleanMovieTitle());
        }

        [Theory]
        [InlineData("Movie.2023.English.HDTV.XviD-LOL[www.abb.com]", "LOL")]
        [InlineData("Movie Title 2023 English HDTV XviD LOL[www.academy.org]", null)]
        [InlineData("Movie Title Now 2023 DVDRip XviD RUNNER[www.aetna.net]", null)]
        [InlineData("Movie.Title.2023.DVDRip.XviD-RUNNER[www.alfaromeo.io]", "RUNNER")]
        [InlineData("Movie.Title.2023.English.HDTV.XviD-LOL[www.abbott.gov]", "LOL")]
        [InlineData("Movie Title 2023 English HDTV XviD LOL[www.actor.org]", null)]
        [InlineData("Movie Title Future 2023 DVDRip XviD RUNNER[www.allstate.net]", null)]
        public void should_not_parse_url_in_group(string title, string expected)
        {
            ReleaseGroupParser.ParseReleaseGroup(title).Should().Be(expected);
        }
    }
}
