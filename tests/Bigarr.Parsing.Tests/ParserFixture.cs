// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/ParserFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using System.Linq;
using FluentAssertions;
using FluentAssertions.Execution;
using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing.Tests
{
    public class ParserFixture
    {
        /*Fucked-up hall of shame,
         * WWE.Wrestlemania.27.PPV.HDTV.XviD-KYR
         * Unreported.World.Chinas.Lost.Sons.WS.PDTV.XviD-FTP
         * [InlineData("Big Time Rush 1x01 to 10 480i DD2 0 Sianto", "Big Time Rush", 1, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, 10)]
         * [InlineData("Desparate Housewives - S07E22 - 7x23 - And Lots of Security.. [HDTV-720p].mkv", "Desparate Housewives", 7, new[] { 22, 23 }, 2)]
         * [InlineData("S07E22 - 7x23 - And Lots of Security.. [HDTV-720p].mkv", "", 7, new[] { 22, 23 }, 2)]
         * (Game of Thrones s03 e - "Game of Thrones Season 3 Episode 10"
         * The.Man.of.Steel.1994-05.33.hybrid.DreamGirl-Novus-HD
         * Superman.-.The.Man.of.Steel.1994-06.34.hybrid.DreamGirl-Novus-HD
         * Superman.-.The.Man.of.Steel.1994-05.33.hybrid.DreamGirl-Novus-HD
         * Constantine S1-E1-WEB-DL-1080p-NZBgeek
         * [InlineData("Valana la Movie FRENCH BluRay 720p 2016 kjhlj", "Valana la Movie")]  Removed 2021-12-19 as this / the regex for this was breaking all movies w/ french in title
         */

        [Fact]
        public void should_remove_accents_from_title()
        {
            const string title = "Carniv\u00E0le";

            title.CleanMovieTitle().Should().Be("carnivale");
        }

        [Theory]
        [InlineData("The.Movie.from.U.N.C.L.E.2015.1080p.BluRay.x264-SPARKS", "The Movie from U.N.C.L.E.")]
        [InlineData("1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", "1776")]
        [InlineData("MY MOVIE (2016) [R][Action, Horror][720p.WEB-DL.AVC.8Bit.6ch.AC3].mkv", "MY MOVIE")]
        [InlineData("R.I.P.D.2013.720p.BluRay.x264-SPARKS", "R.I.P.D.")]
        [InlineData("V.H.S.2.2013.LIMITED.720p.BluRay.x264-GECKOS", "V.H.S. 2")]
        [InlineData("This Is A Movie (1999) [IMDB #] <Genre, Genre, Genre> {ACTORS} !DIRECTOR +MORE_SILLY_STUFF_NO_ONE_NEEDS ?", "This Is A Movie")]
        [InlineData("We Are the Movie!.2013.720p.H264.mkv", "We Are the Movie!")]
        [InlineData("(500).Days.Of.Movie.(2009).DTS.1080p.BluRay.x264.NLsubs", "(500) Days Of Movie")]
        [InlineData("To.Live.and.Movie.in.L.A.1985.1080p.BluRay", "To Live and Movie in L.A.")]
        [InlineData("A.I.Artificial.Movie.(2001)", "A.I. Artificial Movie")]
        [InlineData("A.Movie.Name.(1998)", "A Movie Name")]
        [InlineData("www.Torrenting.com - Movie.2008.720p.X264-DIMENSION", "Movie")]
        [InlineData("www.5MovieRulz.tc - Movie (2000) Malayalam HQ HDRip - x264 - AAC - 700MB.mkv", "Movie")]
        [InlineData("Movie: The Movie World 2013", "Movie: The Movie World")]
        [InlineData("Movie.The.Final.Chapter.2016", "Movie The Final Chapter")]
        [InlineData("Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James")]
        [InlineData("Movie.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Movie")]
        [InlineData("Valana la Movie TRUEFRENCH BluRay 720p 2016 kjhlj", "Valana la Movie")]
        [InlineData("Mission Movie: Rogue Movie (2015)�[XviD - Ita Ac3 - SoftSub Ita]azione, spionaggio, thriller *Prima Visione* Team mulnic Tom Cruise", "Mission Movie: Rogue Movie")]
        [InlineData("Movie.Movie.2000.FRENCH..BluRay.-AiRLiNE", "Movie Movie")]
        [InlineData("My Movie 1999 German Bluray", "My Movie")]
        [InlineData("Leaving Movie by Movie (1897) [DVD].mp4", "Leaving Movie by Movie")]
        [InlineData("Movie.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie")]
        [InlineData("Movie.Title.Imax.2018.1080p.AMZN.WEB-DL.DD5.1.H.264-NTG", "Movie Title")]
        [InlineData("World.Movie.Z.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z")]
        [InlineData("World.Movie.Z.2.EXTENDED.2013.German.DL.1080p.BluRay.AVC-XANOR", "World Movie Z 2")]
        [InlineData("G.I.Movie.Movie.2013.THEATRiCAL.COMPLETE.BLURAY-GLiMMER", "G.I. Movie Movie")]
        [InlineData("www.Torrenting.org - Movie.2008.720p.X264-DIMENSION", "Movie")]
        [InlineData("The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]", "The French Movie")]
        [InlineData("The.Good.German.2006.720p.BluRay.x264-RlsGrp", "The Good German")] // "Hardcoded to exclude from German regex"
        public void should_parse_movie_title(string postTitle, string title)
        {
            Parser.ParseMovieTitle(postTitle).PrimaryMovieTitle.Should().Be(title);
        }

        [Theory]
        [InlineData("[MTBB] Kimi no Na wa. (2016) v2 [97681524].mkv", "Kimi no Na wa", "MTBB", 2016)]
        [InlineData("[sam] Toward the Terra (1980) [BD 1080p TrueHD].mkv", "Toward the Terra", "sam", 1980)]
        public void should_parse_anime_movie_title(string postTitle, string title, string releaseGroup, int year)
        {
            var movie = Parser.ParseMovieTitle(postTitle);
            using (new AssertionScope())
            {
                movie.PrimaryMovieTitle.Should().Be(title);
                movie.ReleaseGroup.Should().Be(releaseGroup);
                movie.Year.Should().Be(year);
            }
        }

        [Theory]
        [InlineData("[Arid] Cowboy Bebop - Knockin' on Heaven's Door v2 [00F4CDA0].mkv", "Cowboy Bebop - Knockin' on Heaven's Door", "Arid")]
        [InlineData("[Baws] Evangelion 1.11 - You Are (Not) Alone v2 (1080p BD HEVC FLAC) [BF42B1C8].mkv", "Evangelion 1 11 - You Are (Not) Alone", "Baws")]
        [InlineData("[Arid] 5 Centimeters per Second (BDRip 1920x1080 Hi10 FLAC) [FD8B6FF2].mkv", "5 Centimeters per Second", "Arid")]
        [InlineData("[Baws] Evangelion 2.22 - You Can (Not) Advance (1080p BD HEVC FLAC) [56E7A5B8].mkv", "Evangelion 2 22 - You Can (Not) Advance", "Baws")]
        [InlineData("[sam] Goblin Slayer - Goblin's Crown [BD 1080p FLAC] [CD298D48].mkv", "Goblin Slayer - Goblin's Crown", "sam")]
        [InlineData("[Kulot] Violet Evergarden Gaiden Eien to Jidou Shuki Ningyou [Dual-Audio][BDRip 1920x804 HEVC FLACx2] [91FC62A8].mkv", "Violet Evergarden Gaiden Eien to Jidou Shuki Ningyou", "Kulot")]
        public void should_parse_anime_movie_title_without_year(string postTitle, string title, string releaseGroup)
        {
            var movie = Parser.ParseMovieTitle(postTitle);
            using (new AssertionScope())
            {
                movie.PrimaryMovieTitle.Should().Be(title);
                movie.ReleaseGroup.Should().Be(releaseGroup);
            }
        }

        [Theory]
        [InlineData("Movie.Aufbruch.nach.Pandora.Extended.2009.German.DTS.720p.BluRay.x264-SoW", "Movie Aufbruch nach Pandora", "Extended", 2009)]
        [InlineData("Drop.Movie.1994.German.AC3D.DL.720p.BluRay.x264-KLASSiGERHD", "Drop Movie", "", 1994)]
        [InlineData("Kick.Movie.2.2013.German.DTS.DL.720p.BluRay.x264-Pate", "Kick Movie 2", "", 2013)]
        [InlineData("Movie.Hills.2019.German.DL.AC3.Dubbed.1080p.BluRay.x264-muhHD", "Movie Hills", "", 2019)]
        [InlineData("96.Hours.Movie.3.EXTENDED.2014.German.DL.1080p.BluRay.x264-ENCOUNTERS", "96 Hours Movie 3", "EXTENDED", 2014)]
        [InlineData("Movie.War.Q.EXTENDED.CUT.2013.German.DL.1080p.BluRay.x264-HQX", "Movie War Q", "EXTENDED CUT", 2013)]
        [InlineData("Sin.Movie.2005.RECUT.EXTENDED.German.DL.1080p.BluRay.x264-DETAiLS", "Sin Movie", "RECUT EXTENDED", 2005)]
        [InlineData("2.Movie.in.L.A.1996.GERMAN.DL.720p.WEB.H264-SOV", "2 Movie in L.A.", "", 1996)]
        [InlineData("8.2019.GERMAN.720p.BluRay.x264-UNiVERSUM", "8", "", 2019)]
        [InlineData("Life.Movie.2014.German.DL.PAL.DVDR-ETM", "Life Movie", "", 2014)]
        [InlineData("Joe.Movie.2.EXTENDED.EDITION.2015.German.DL.PAL.DVDR-ETM", "Joe Movie 2", "EXTENDED EDITION", 2015)]
        [InlineData("Movie.EXTENDED.2011.HDRip.AC3.German.XviD-POE", "Movie", "EXTENDED", 2011)]

        // Special cases (see description)
        [InlineData("Movie.Klasse.von.1999.1990.German.720p.HDTV.x264-NORETAiL", "Movie Klasse von 1999", "", 1990)] // "year in the title"
        [InlineData("Movie.Squad.2016.EXTENDED.German.DL.AC3.BDRip.x264-hqc", "Movie Squad", "EXTENDED", 2016)] // "edition after year"
        [InlineData("Movie.and.Movie.2010.Extended.Cut.German.DTS.DL.720p.BluRay.x264-HDS", "Movie and Movie", "Extended Cut", 2010)] // "edition after year"
        [InlineData("Der.Movie.James.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", "Der Movie James", "", 1998)] // "year at the end"
        [InlineData("Der.Movie.Eine.Unerwartete.Reise.Extended.German.720p.BluRay.x264-EXQUiSiTE", "Der Movie Eine Unerwartete Reise", "Extended", 0)] // "no year & edition"
        [InlineData("Movie.Weg.des.Kriegers.EXTENDED.German.720p.BluRay.x264-EXQUiSiTE", "Movie Weg des Kriegers", "EXTENDED", 0)] // "no year & edition"
        [InlineData("Die.Unfassbaren.Movie.Name.EXTENDED.German.DTS.720p.BluRay.x264-RHD", "Die Unfassbaren Movie Name", "EXTENDED", 0)] // "no year & edition"
        [InlineData("Die Unfassbaren Movie Name EXTENDED German DTS 720p BluRay x264-RHD", "Die Unfassbaren Movie Name", "EXTENDED", 0)] // "no year & edition & without dots"
        [InlineData("Passengers.German.DL.AC3.Dubbed..BluRay.x264-PsO", "Passengers", "", 0)] // "no year"
        [InlineData("Das.A.Team.Der.Film.Extended.Cut.German.720p.BluRay.x264-ANCIENT", "Das A Team Der Film", "Extended Cut", 0)] // "no year"
        [InlineData("Cars.2.German.DL.720p.BluRay.x264-EmpireHD", "Cars 2", "", 0)] // "no year"
        [InlineData("Die.fantastische.Reise.des.Dr.Dolittle.2020.German.DL.LD.1080p.WEBRip.x264-PRD", "Die fantastische Reise des Dr. Dolittle", "", 2020)] // "dot after dr"
        [InlineData("Der.Film.deines.Lebens.German.2011.PAL.DVDR-ETM", "Der Film deines Lebens", "", 2011)] // "year at wrong position"
        [InlineData("Kick.Ass.2.2013.German.DTS.DL.720p.BluRay.x264-Pate_", "Kick Ass 2", "", 2013)] // "underscore at the end"
        [InlineData("The.Good.German.2006.GERMAN.720p.HDTV.x264-RLsGrp", "The Good German", "", 2006)] // "German in the title"
        public void should_parse_german_movie(string postTitle, string title, string edition, int year)
        {
            var movie = Parser.ParseMovieTitle(postTitle);
            using (new AssertionScope())
            {
                movie.PrimaryMovieTitle.Should().Be(title);
                movie.Edition.Should().Be(edition);
                movie.Year.Should().Be(year);
            }
        }

        [Theory]
        [InlineData("L'hypothèse.du.movie.volé.AKA.The.Hypothesis.of.the.Movie.Title.1978.1080p.CINET.WEB-DL.AAC2.0.x264-Cinefeel.mkv",
            new string[]
            {
                "L'hypothèse du movie volé AKA The Hypothesis of the Movie Title",
                "L'hypothèse du movie volé",
                "The Hypothesis of the Movie Title"
            })]
        [InlineData("Skjegg.AKA.Rox.Beard.1965.CD1.CRiTERiON.DVDRip.XviD-KG.avi",
            new string[]
            {
                "Skjegg AKA Rox Beard",
                "Skjegg",
                "Rox Beard"
            })]
        [InlineData("Kjeller.chitai.AKA.Basement.of.Shame.1956.1080p.BluRay.x264.FLAC.1.0.mkv",
            new string[]
            {
                "Kjeller chitai AKA Basement of Shame",
                "Kjeller chitai",
                "Basement of Shame"
            })]
        [InlineData("Radarr.Under.Water.(aka.Beneath.the.Code.Freeze).1997.DVDRip.x264.CG-Grzechsin.mkv",
            new string[]
            {
                "Radarr Under Water (aka Beneath the Code Freeze)",
                "Radarr Under Water",
                "Beneath the Code Freeze"
            })]
        [InlineData("Radarr.prodavet. AKA.Radarr.Shift.2005.DVDRip.x264-HANDJOB.mkv",
            new string[]
            {
                "Radarr prodavet  AKA Radarr Shift",
                "Radarr prodavet",
                "Radarr Shift"
            })]
        [InlineData("AKA.2002.DVDRip.x264-HANDJOB.mkv",
            new string[]
            {
                "AKA"
            })]
        [InlineData("KillRoyWasHere.2000.BluRay.1080p.DTS.x264.dxva-EuReKA.mkv",
            new string[]
            {
                "KillRoyWasHere"
            })]
        [InlineData("Aka Rox (2008).avi",
            new string[]
            {
                "Aka Rox"
            })]
        [InlineData("Return Earth to Normal 'em High aka World 2 (2022) 1080p.mp4",
            new string[]
            {
                "Return Earth to Normal 'em High aka World 2",
                "Return Earth to Normal 'em High",
                "World 2"
            })]
        [InlineData("Енола Голмс / Enola Holmes (2020) UHD WEB-DL 2160p 4K HDR H.265 Ukr/Eng | Sub Ukr/Eng",
            new string[]
            {
                "Енола Голмс / Enola Holmes",
                "Енола Голмс",
                "Enola Holmes"
            })]
        [InlineData("Mon cousin a.k.a. My Cousin 2020 1080p Blu-ray DD 5.1 x264.mkv",
            new string[]
            {
                "Mon cousin AKA My Cousin",
                "Mon cousin",
                "My Cousin"
            })]
        [InlineData("Sydney A.K.A. Hard Eight 1996 1080p AMZN WEB-DL DD+ 2.0 H.264.mkv",
            new string[]
            {
                "Sydney AKA Hard Eight",
                "Sydney",
                "Hard Eight"
            })]
        public void should_parse_movie_alternative_titles(string postTitle, string[] parsedTitles)
        {
            var movieInfo = Parser.ParseMovieTitle(postTitle, true);

            movieInfo.MovieTitles.Count.Should().Be(parsedTitles.Length);

            for (var i = 0; i < movieInfo.MovieTitles.Count; i += 1)
            {
                movieInfo.MovieTitles[i].Should().Be(parsedTitles[i]);
            }
        }

        [Theory]
        [InlineData("(1995) Movie Name", "Movie Name")]
        public void should_parse_movie_folder_name(string postTitle, string title)
        {
            Parser.ParseMovieTitle(postTitle, true).PrimaryMovieTitle.Should().Be(title);
        }

        [Theory]
        [InlineData("1776.1979.EXTENDED.720p.BluRay.X264-AMIABLE", 1979)]
        [InlineData("Movie Name FRENCH BluRay 720p 2016 kjhlj", 2016)]
        [InlineData("Der.Movie.German.Bluray.FuckYou.Pso.Why.cant.you.follow.scene.rules.1998", 1998)]
        [InlineData("Movie Name (1897) [DVD].mp4", 1897)]
        [InlineData("World Movie Z Movie [2023]", 2023)]
        public void should_parse_movie_year(string postTitle, int year)
        {
            Parser.ParseMovieTitle(postTitle).Year.Should().Be(year);
        }

        [Theory]
        [InlineData("Movie Name (2016) {tmdbid-43074}", 43074)]
        [InlineData("Movie Name (2016) [tmdb-43074]", 43074)]
        [InlineData("Movie Name (2016) {tmdb-43074}", 43074)]
        [InlineData("Movie Name (2016) {tmdb-2020}", 2020)]
        public void should_parse_tmdb_id(string postTitle, int tmdbId)
        {
            Parser.ParseMovieTitle(postTitle).TmdbId.Should().Be(tmdbId);
        }

        [Theory]
        [InlineData("The.Italian.Movie.2025.720p.BluRay.X264-AMIABLE")]
        [InlineData("The.French.Movie.2013.720p.BluRay.x264 - ROUGH[PublicHD]")]
        [InlineData("The.German.Doctor.2013.LIMITED.DVDRip.x264-RedBlade")] // "When German is not followed by a year or a SCENE word it is not matched"
        [InlineData("The.Good.German.2006.720p.HDTV.x264-TVP")] // "The Good German is hardcoded not to match"
        [InlineData("German.Lancers.2019.720p.BluRay.x264-UNiVERSUM")] // "German at the beginning is never matched"
        [InlineData("The.German.2019.720p.BluRay.x264-UNiVERSUM")] // "The German is hardcoded not to match"
        [InlineData("Movie Name (2016) BluRay 1080p DTS-ES AC3 x264-3Li")] // "DTS-ES should not match ES (Spanish)"
        public void should_not_parse_wrong_language_in_title(string postTitle)
        {
            var parsed = Parser.ParseMovieTitle(postTitle, true);
            parsed.Languages.Count.Should().Be(1);
            parsed.Languages.First().Should().Be(Language.Unknown);
        }

        [Theory]
        [InlineData("Movie.Title.2016.1080p.KORSUB.WEBRip.x264.AAC2.0-RADARR", "KORSUB")]
        [InlineData("Movie.Title.2016.1080p.KORSUBS.WEBRip.x264.AAC2.0-RADARR", "KORSUBS")]
        [InlineData("Movie Title 2017 HC 720p HDRiP DD5 1 x264-LEGi0N", "Generic Hardcoded Subs")]
        [InlineData("Movie.Title.2017.720p.SUBBED.HDRip.V2.XViD-26k.avi", "Generic Hardcoded Subs")]
        [InlineData("Movie.Title.2000.1080p.BlueRay.x264.DTS.RoSubbed-playHD", null)]
        [InlineData("Movie Title! 2018 [Web][MKV][h264][480p][AAC 2.0][Softsubs]", null)]
        [InlineData("Movie Title! 2019 [HorribleSubs][Web][MKV][h264][848x480][AAC 2.0][Softsubs(HorribleSubs)]", null)]
        [InlineData("Movie Title! 2024 [Web][x265][1080p][EAC3][MultiSubs]", null)]
        public void should_parse_hardcoded_subs(string postTitle, string sub)
        {
            Parser.ParseMovieTitle(postTitle).HardcodedSubs.Should().Be(sub);
        }

        [Theory]
        [InlineData("That Italian Movie 2008 [tt1234567] 720p BluRay X264", "tt1234567")]
        [InlineData("That Italian Movie 2008 [tt12345678] 720p BluRay X264", "tt12345678")]
        public void should_parse_imdb_in_title(string postTitle, string imdb)
        {
            var parsed = Parser.ParseMovieTitle(postTitle, true);
            parsed.ImdbId.Should().Be(imdb);
        }

        [Theory]
        [InlineData("asfd", null)]
        [InlineData("123", "tt0000123")]
        [InlineData("1234567", "tt1234567")]
        [InlineData("tt1234567", "tt1234567")]
        [InlineData("tt12345678", "tt12345678")]
        [InlineData("12345678", "tt12345678")]
        public void should_normalize_imdbid(string imdbid, string normalized)
        {
            Parser.NormalizeImdbId(imdbid).Should().BeEquivalentTo(normalized);
        }
    }
}
