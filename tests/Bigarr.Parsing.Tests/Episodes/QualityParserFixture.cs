// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/QualityParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using Bigarr.Parsing.Qualities;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class QualityParserFixture
    {
        // Bigarr: Sonarr names its remux qualities "Bluray-1080p Remux" / "Bluray-2160p Remux"; the shared
        // catalogue keeps Radarr's "Remux-1080p" / "Remux-2160p". Sonarr's own names are what this test feeds.
        private static string SonarrQualityName(Quality quality)
        {
            if (quality == Quality.Remux1080p)
            {
                return "Bluray-1080p Remux";
            }

            if (quality == Quality.Remux2160p)
            {
                return "Bluray-2160p Remux";
            }

            return quality.Name;
        }

        public static IEnumerable<object[]> SelfQualityParserCases = new[]
        {
            new object[] { Quality.SDTV },
            new object[] { Quality.DVD },
            new object[] { Quality.WEBDL480p },
            new object[] { Quality.Bluray480p },
            new object[] { Quality.Bluray576p },
            new object[] { Quality.HDTV720p },
            new object[] { Quality.HDTV1080p },
            new object[] { Quality.HDTV2160p },
            new object[] { Quality.WEBDL720p },
            new object[] { Quality.WEBDL1080p },
            new object[] { Quality.WEBDL2160p },
            new object[] { Quality.Bluray720p },
            new object[] { Quality.Bluray1080p },
            new object[] { Quality.Bluray2160p },
            new object[] { Quality.Remux1080p },
            new object[] { Quality.Remux2160p },
        };

        public static IEnumerable<object[]> OtherSourceQualityParserCases = new[]
        {
            new object[] { "SD TV", Quality.SDTV },
            new object[] { "SD DVD",  Quality.DVD },
            new object[] { "480p WEB-DL", Quality.WEBDL480p },
            new object[] { "HD TV", Quality.HDTV720p },
            new object[] { "1080p HD TV", Quality.HDTV1080p },
            new object[] { "2160p HD TV", Quality.HDTV2160p },
            new object[] { "720p WEB-DL", Quality.WEBDL720p },
            new object[] { "1080p WEB-DL", Quality.WEBDL1080p },
            new object[] { "2160p WEB-DL", Quality.WEBDL2160p },
            new object[] { "720p BluRay", Quality.Bluray720p },
            new object[] { "1080p BluRay", Quality.Bluray1080p },
            new object[] { "2160p BluRay", Quality.Bluray2160p },
            new object[] { "1080p Remux", Quality.Remux1080p },
            new object[] { "2160p Remux", Quality.Remux2160p },
        };

        [Theory]
        [InlineData("S07E23 .avi ", false)]
        [InlineData("The.Series.S01E13.x264-CtrlSD", false)]
        [InlineData("The Series S02E01 HDTV XviD 2HD", false)]
        [InlineData("The Series S05E11 PROPER HDTV XviD 2HD", true)]
        [InlineData("The Series Show S02E08 HDTV x264 FTP", false)]
        [InlineData("The.Series.2011.S02E01.WS.PDTV.x264-TLA", false)]
        [InlineData("The.Series.2011.S02E01.WS.PDTV.x264-REPACK-TLA", true)]
        [InlineData("The Series S01E04 DSR x264 2HD", false)]
        [InlineData("The Series S01E04 Series Death Train DSR x264 MiNDTHEGAP", false)]
        [InlineData("The Series S11E03 has no periods or extension HDTV", false)]
        [InlineData("The.Series.S04E05.HDTV.XviD-LOL", false)]
        [InlineData("The.Series.S02E15.avi", false)]
        [InlineData("The.Series.S02E15.xvid", false)]
        [InlineData("The.Series.S02E15.divx", false)]
        [InlineData("The.Series.S03E06.HDTV-WiDE", false)]
        [InlineData("Series.S10E27.WS.DSR.XviD-2HD", false)]
        [InlineData("[HorribleSubs] The Series - 32 [480p]", false)]
        [InlineData("[CR] The Series - 004 [480p][48CE2D0F]", false)]
        [InlineData("[Hatsuyuki] The Series - 363 [848x480][ADE35E38]", false)]
        [InlineData("The.Series.S03.TVRip.XviD-NOGRP", false)]
        [InlineData("[HorribleSubs] The Series - 03 [360p].mkv", false)]
        [InlineData("[SubsPlease] Series Title (540p) [AB649D32].mkv", false)]
        [InlineData("[Erai-raws] Series Title [540p][Multiple Subtitle].mkv", false)]
        public void should_parse_sdtv_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.SDTV, proper);
        }

        [Theory]
        [InlineData("The.Series.S01E13.NTSC.x264-CtrlSD", false)]
        [InlineData("The.Series.S03E06.DVDRip.XviD-WiDE", false)]
        [InlineData("The.Series.S03E06.DVD.Rip.XviD-WiDE", false)]
        [InlineData("the.Series.1x13.circles.ws.xvidvd-tns", false)]
        [InlineData("the_Series.9x18.sunshine_days.ac3.ws_dvdrip_xvid-fov.avi", false)]
        [InlineData("[FroZen] Series - 23 [DVD][7F6170E6]", false)]
        [InlineData("[AniDL] Series - 26 -[360p][DVD][D - A][Exiled - Destiny]", false)]
        public void should_parse_dvd_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.DVD, proper);
        }

        [Theory]
        [InlineData("The.Series.S01E10.The.Leviathan.480p.WEB-DL.x264-mSD", false)]
        [InlineData("The.Series.S04E10.Glee.Actually.480p.WEB-DL.x264-mSD", false)]
        [InlineData("The.SeriesS06E11.The.Santa.Simulation.480p.WEB-DL.x264-mSD", false)]
        [InlineData("The.Series.S02E04.480p.WEB.DL.nSD.x264-NhaNc3", false)]
        [InlineData("The.Series.S01E08.Das.geloeschte.Ich.German.Dubbed.DL.AmazonHD.x264-TVS", false)]
        [InlineData("The.Series.S01E04.Rod.Trip.mit.meinem.Onkel.German.DL.NetflixUHD.x264", false)]
        [InlineData("[HorribleSubs] Series Title! S01 [Web][MKV][h264][480p][AAC 2.0][Softsubs (HorribleSubs)]", false)]
        [InlineData("Series.Title.S13E11.Ausgebacken.German.AmazonSD.h264-4SF", false)]
        public void should_parse_webdl480p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBDL480p, proper);
        }

        [Theory]
        [InlineData("SERIES.S03E01-06.DUAL.XviD.Bluray.AC3-REPACK.-HELLYWOOD.avi", true)]
        [InlineData("SERIES.S03E01-06.DUAL.BDRip.XviD.AC3.-HELLYWOOD", false)]
        [InlineData("SERIES.S03E01-06.DUAL.BDRip.X-viD.AC3.-HELLYWOOD", false)]
        [InlineData("SERIES.S03E01-06.DUAL.BDRip.AC3.-HELLYWOOD", false)]
        [InlineData("SERIES.S03E01-06.DUAL.BDRip.XviD.AC3.-HELLYWOOD.avi", false)]
        [InlineData("SERIES.S03E01-06.DUAL.XviD.Bluray.AC3.-HELLYWOOD.avi", false)]
        [InlineData("The.Series.S01E05.480p.BluRay.DD5.1.x264-HiSD", false)]
        [InlineData("The Series (BD)(640x480(RAW) (BATCH 1) (1-13)", false)]
        [InlineData("[Doki] Series - 02 (848x480 XviD BD MP3) [95360783]", false)]
        [InlineData("Adventures.of.Sonic.the.Hedgehog.S01.BluRay.480i.DD.2.0.AVC.REMUX-FraMeSToR", false)]
        [InlineData("Adventures.of.Sonic.the.Hedgehog.S01E01.Best.Hedgehog.480i.DD.2.0.AVC.REMUX-FraMeSToR", false)]
        public void should_parse_bluray480p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Bluray480p, proper);
        }

        [Theory]
        [InlineData("The.Series.S02E10.480p.HULU.WEBRip.x264-Puffin", false)]
        [InlineData("The.Series.S10E14.Techs.And.Balances.480p.AE.WEBRip.AAC2.0.x264-SEA", false)]
        [InlineData("Series.Title.1x04.ITA.WEBMux.x264-NovaRip", false)]
        public void should_parse_webrip480p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBRip480p, proper);
        }

        [Theory]
        [InlineData("The.Series.S01E05.576p.BluRay.DD5.1.x264-HiSD", false)]
        public void should_parse_bluray576p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Bluray576p, proper);
        }

        [Theory]
        [InlineData("Series - S01E01 - Title [HDTV]", false)]
        [InlineData("Series - S01E01 - Title [HDTV-720p]", false)]
        [InlineData("The Series S04E87 REPACK 720p HDTV x264 aAF", true)]
        [InlineData("The.Series.S02E15.720p", false)]
        [InlineData("S07E23 - [HDTV-720p].mkv ", false)]
        [InlineData("Series - S22E03 - MoneyBART - HD TV.mkv", false)]
        [InlineData("S07E23.mkv ", false)]
        [InlineData("The.Series.S08E05.720p.HDTV.X264-DIMENSION", false)]
        [InlineData("The.Series.S02E15.mkv", false)]
        [InlineData(@"E:\Downloads\tv\The.Series.S01E01.720p.HDTV\ajifajjjeaeaeqwer_eppj.avi", false)]
        [InlineData("The.Series.S01E08.Tourmaline.Nepal.720p.HDTV.x264-DHD", false)]
        [InlineData("[Underwater-FFF] The Series - 01 (720p) [27AAA0A0]", false)]
        [InlineData("[Doki] The Series - 07 (1280x720 Hi10P AAC) [80AF7DDE]", false)]
        [InlineData("[Doremi].The.Series.5.Go.Go!.31.[1280x720].[C65D4B1F].mkv", false)]
        [InlineData("[HorribleSubs]_Series_Title_-_145_[720p]", false)]
        [InlineData("[Eveyuu] Series Title - 10 [Hi10P 1280x720 H264][10B23BD8]", false)]
        [InlineData("The.Series.US.S12E17.HR.WS.PDTV.X264-DIMENSION", false)]
        [InlineData("The.Series.The.Lost.Sonarr.Summer.HR.WS.PDTV.x264-DHD", false)]
        [InlineData("The Series S01E07 - Motor zmen (CZ)[TvRip][HEVC][720p]", false)]
        [InlineData("The.Series.S05E06.720p.HDTV.x264-FHD", false)]
        [InlineData("Series.Title.1x01.ITA.720p.x264-RlsGrp [01/54] - \"series.title.1x01.ita.720p.x264-rlsgrp.nfo\"", false)]
        [InlineData("[TMS-Remux].Series.Title.X.21.720p.[76EA1C53].mkv", false)]
        public void should_parse_hdtv720p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.HDTV720p, proper);
        }

        [Theory]
        [InlineData("Under the Series S01E10 Let the Sonarr Begin 1080p", false)]
        [InlineData("Series.S07E01.ARE.YOU.1080P.HDTV.X264-QCF", false)]
        [InlineData("Series.S07E01.ARE.YOU.1080P.HDTV.x264-QCF", false)]
        [InlineData("Series.S07E01.ARE.YOU.1080P.HDTV.proper.X264-QCF", true)]
        [InlineData("Series - S01E01 - Title [HDTV-1080p]", false)]
        [InlineData("[HorribleSubs] Series Title - 32 [1080p]", false)]
        [InlineData("Series S01E07 - Sonarr zmen (CZ)[TvRip][HEVC][1080p]", false)]
        [InlineData("The Online Series Alicization 04 vostfr FHD", false)]
        [InlineData("Series Slayer 04 vostfr FHD.mkv", false)]
        [InlineData("[Onii-ChanSub] The.Series - 02 vostfr (FHD 1080p 10bits).mkv", false)]
        [InlineData("[Miaou] Series Title 02 VOSTFR FHD 10 bits", false)]
        [InlineData("[mhastream.com]_Episode_05_FHD.mp4", false)]
        [InlineData("[Kousei]_One_Series_ - _609_[FHD][648A87C7].mp4", false)]
        [InlineData("Series culpable 1x02 Culpabilidad [HDTV 1080i AVC MP2 2.0 Sub][GrupoHDS]", false)]
        [InlineData("Series cómo pasó - 19x15 [344] Cuarenta años de baile [HDTV 1080i AVC MP2 2.0 Sub][GrupoHDS]", false)]
        [InlineData("Super.Seires.Go.S01E02.Depths.of.Sonarr.1080i.HDTV.DD5.1.H.264-NOGRP", false)]
        public void should_parse_hdtv1080p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.HDTV1080p, proper);
        }

        [Theory]
        [InlineData("My Title - S01E01 - EpTitle [HEVC 4k DTSHD-MA-6ch]", false)]
        [InlineData("My Title - S01E01 - EpTitle [HEVC-4k DTSHD-MA-6ch]", false)]
        [InlineData("My Title - S01E01 - EpTitle [4k HEVC DTSHD-MA-6ch]", false)]
        [InlineData("[GM-Team][国漫][诛仙][Series Title][2022][19][HEVC][GB][4K]", false)]
        public void should_parse_hdtv2160p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.HDTV2160p, proper);
        }

        [Theory]
        [InlineData("Series S01E04 Mexicos Death Train 720p WEB DL", false)]
        [InlineData("Series Five 0 S02E21 720p WEB DL DD5 1 H 264", false)]
        [InlineData("Series S04E22 720p WEB DL DD5 1 H 264 NFHD", false)]
        [InlineData("Series - S11E06 - D-Yikes! - 720p WEB-DL.mkv", false)]
        [InlineData("The.Series.S02E15.720p.WEB-DL.DD5.1.H.264-SURFER", false)]
        [InlineData("S07E23 - [WEBDL].mkv ", false)]
        [InlineData("Series S04E22 720p WEB-DL DD5.1 H264-EbP.mkv", false)]
        [InlineData("Series.S04.720p.Web-Dl.Dd5.1.h264-P2PACK", false)]
        [InlineData("Da.Series.Shows.S02E04.720p.WEB.DL.nSD.x264-NhaNc3", false)]
        [InlineData("Series.Miami.S04E25.720p.iTunesHD.AVC-TVS", false)]
        [InlineData("Series.S06E23.720p.WebHD.h264-euHD", false)]
        [InlineData("Series.Title.2016.03.14.720p.WEB.x264-spamTV", false)]
        [InlineData("Series.Title.2016.03.14.720p.WEB.h264-spamTV", false)]
        [InlineData("Series.S01E08.Das.geloeschte.Ich.German.DD51.Dubbed.DL.720p.AmazonHD.x264-TVS", false)]
        [InlineData("Series.Polo.S01E11.One.Hundred.Sonarrs.2015.German.DD51.DL.720p.NetflixUHD.x264.NewUp.by.Wunschtante", false)]
        [InlineData("Series 2016 German DD51 DL 720p NetflixHD x264-TVS", false)]
        [InlineData("Series.6x10.Basic.Sonarr.Repair.and.Replace.ITA.ENG.720p.WEB-DLMux.H.264-GiuseppeTnT", false)]
        [InlineData("Series.6x11.Modern.Spy.ITA.ENG.720p.WEB.DLMux.H.264-GiuseppeTnT", false)]
        [InlineData("The Series Was Dead 2010 S09E13 [MKV / H.264 / AC3/AAC / WEB / Dual Áudio / Inglês / 720p]", false)]
        [InlineData("into.the.Series.s03e16.h264.720p-web-handbrake.mkv", false)]
        [InlineData("Series.S01E01.The.Sonarr.Principle.720p.WEB-DL.DD5.1.H.264-BD", false)]
        [InlineData("Series.S03E05.Griebnitzsee.German.720p.MaxdomeHD.AVC-TVS", false)]
        [InlineData("[HorribleSubs] Series Title! S01 [Web][MKV][h264][720p][AAC 2.0][Softsubs (HorribleSubs)]", false)]
        [InlineData("[HorribleSubs] Series Title! S01 [Web][MKV][h264][AAC 2.0][Softsubs (HorribleSubs)]", false)]
        [InlineData("Series.Title.S04E13.960p.WEB-DL.AAC2.0.H.264-squalor", false)]
        [InlineData("Series.Title.S16.DP.WEB.720p.DDP.5.1.H.264.PLEX", false)]
        [InlineData("Series.Title.S01E01.Erste.Begegnungen.German.DD51.Synced.DL.720p.HBOMaxHD.AVC-TVS", false)]
        [InlineData("Series.Title.S01E05.Tavora.greift.an.German.DL.720p.DisneyHD.h264-4SF", false)]
        public void should_parse_webdl720p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBDL720p, proper);
        }

        [Theory]
        [InlineData("Series.Title.S04E01.720p.WEBRip.AAC2.0.x264-NFRiP", false)]
        [InlineData("Series.Title.S01E07.A.Prayer.For.Mad.Sweeney.720p.AMZN.WEBRip.DD5.1.x264-NTb", false)]
        [InlineData("Series.Title.S07E01.A.New.Home.720p.DSNY.WEBRip.AAC2.0.x264-TVSmash", false)]
        [InlineData("Series.Title.1x04.ITA.720p.WEBMux.x264-NovaRip", false)]
        public void should_parse_webrip720p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBRip720p, proper);
        }

        [Theory]
        [InlineData("Series S09E03 1080p WEB DL DD5 1 H264 NFHD", false)]
        [InlineData("Two and a Half Developers of the Series S10E03 1080p WEB DL DD5 1 H 264 NFHD", false)]
        [InlineData("Series.S08E01.1080p.WEB-DL.DD5.1.H264-NFHD", false)]
        [InlineData("Its.Always.Sonarrs.Fault.S08E01.1080p.WEB-DL.proper.AAC2.0.H.264", true)]
        [InlineData("This is an Easter Egg S10E03 1080p WEB DL DD5 1 H 264 REPACK NFHD", true)]
        [InlineData("Series.S04E09.Swan.Song.1080p.WEB-DL.DD5.1.H.264-ECI", false)]
        [InlineData("The.Big.Easter.Theory.S06E11.The.Sonarr.Simulation.1080p.WEB-DL.DD5.1.H.264", false)]
        [InlineData("Sonarr's.Baby.S01E02.Night.2.[WEBDL-1080p].mkv", false)]
        [InlineData("Series.Title.2016.03.14.1080p.WEB.x264-spamTV", false)]
        [InlineData("Series.Title.2016.03.14.1080p.WEB.h264-spamTV", false)]
        [InlineData("Series.S01.1080p.WEB-DL.AAC2.0.AVC-TrollHD", false)]
        [InlineData("Series Title S06E08 1080p WEB h264-EXCLUSIVE", false)]
        [InlineData("Series Title S06E08 No One PROPER 1080p WEB DD5 1 H 264-EXCLUSIVE", true)]
        [InlineData("Series Title S06E08 No One PROPER 1080p WEB H 264-EXCLUSIVE", true)]
        [InlineData("The.Series.S25E21.Pay.No1.1080p.WEB-DL.DD5.1.H.264-NTb", false)]
        [InlineData("Series.S01E08.Das.geloeschte.Ich.German.DD51.Dubbed.DL.1080p.AmazonHD.x264-TVS", false)]
        [InlineData("Death.Series.2017.German.DD51.DL.1080p.NetflixHD.x264-TVS", false)]
        [InlineData("Series.S01E08.Pro.Gamer.1440p.BKPL.WEB-DL.H.264-LiGHT", false)]
        [InlineData("Series.Title.S04E11.Teddy's.Choice.FHD.1080p.Web-DL", false)]
        [InlineData("Series.S04E03.The.False.Bride.1080p.NF.WEB.DDP5.1.x264-NTb[rartv]", false)]
        [InlineData("Series.Title.S02E02.This.Year.Will.Be.Different.1080p.AMZN.WEB...", false)]
        [InlineData("Series.Title.S02E02.This.Year.Will.Be.Different.1080p.AMZN.WEB.", false)]
        [InlineData("Series Title - S01E11 2020 1080p Viva MKV WEB", false)]
        [InlineData("[HorribleSubs] Series Title! S01 [Web][MKV][h264][1080p][AAC 2.0][Softsubs (HorribleSubs)]", false)]
        [InlineData("[LostYears] Series Title - 01-17 (WEB 1080p x264 10-bit AAC) [Dual-Audio]", false)]
        [InlineData("Series.and.Titles.S01.1080p.NF.WEB.DD2.0.x264-SNEAkY", false)]
        [InlineData("Series.Title.S02E02.This.Year.Will.Be.Different.1080p.WEB.H 265", false)]
        [InlineData("Series Title Season 2 [WEB 1080p HEVC Opus] [Netaro]", false)]
        [InlineData("Series Title Season 2 (WEB 1080p HEVC Opus) [Netaro]", false)]
        [InlineData("Series.Title.S01E01.Erste.Begegnungen.German.DD51.Synced.DL.1080p.HBOMaxHD.AVC-TVS", false)]
        [InlineData("Series.Title.S01E05.Tavora.greift.an.German.DL.1080p.DisneyHD.h264-4SF", false)]
        [InlineData("Series.Title.S02E04.German.Dubbed.DL.AAC.1080p.WEB.AVC-GROUP", false)]
        [InlineData("Tiny.Series.2020.DOC.S01.MULTi.1080p.ATVP.WEB.Atmos.H.265-TFA", false)]
        [InlineData("The.Series.S25E21.Pay.No1.1080p.WEB-DL.DD5.1.H.266-GROUP", false)]
        [InlineData("Series.Title.S04E01.1080p.WEB.H266-GROUP", false)]
        public void should_parse_webdl1080p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBDL1080p, proper);
        }

        [Theory]
        [InlineData("Series.Title.S04E01.iNTERNAL.1080p.WEBRip.x264-QRUS", false)]
        [InlineData("Series.Title.S07E20.1080p.AMZN.WEBRip.DDP5.1.x264-ViSUM ac3.(NLsub)", false)]
        [InlineData("Series.Title.S03E09.1080p.NF.WEBRip.DD5.1.x264-ViSUM", false)]
        [InlineData("The Series 42 S09E13 1.54 GB WEB-RIP 1080p Dual-Audio 2019 MKV", false)]
        [InlineData("Series.Title.1x04.ITA.1080p.WEBMux.x264-NovaRip", false)]
        [InlineData("Series.Title.2019.S02E07.Chapter.15.The.Believer.4Kto1080p.DSNYP.Webrip.x265.10bit.EAC3.5.1.Atmos.GokiTAoE", false)]
        [InlineData("Series.Title.S01.1080p.AMZN.WEB-Rip.DDP5.1.H.264-Telly", false)]
        [InlineData("Series Title S01 WEBRip 1080p x266-GROUP", false)]
        [InlineData("Series Title S01 Complete German 1080p HDR UHD WEBRip h266-GROUP", false)]
        public void should_parse_webrip1080p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBRip1080p, proper);
        }

        [Theory]
        [InlineData("Series.Title.2016.03.14.2160p.WEB.x264-spamTV", false)]
        [InlineData("Series.Title.2016.03.14.2160p.WEB.h264-spamTV", false)]
        [InlineData("Series.Title.2016.03.14.2160p.WEB.PROPER.h264-spamTV", true)]
        [InlineData("House.of.Sonarr.AK.s05e13.4K.UHD.WEB.DL", false)]
        [InlineData("House.of.Sonarr.AK.s05e13.UHD.4K.WEB.DL", false)]
        [InlineData("[HorribleSubs] Series Title! S01 [Web][MKV][h264][2160p][AAC 2.0][Softsubs (HorribleSubs)]", false)]
        [InlineData("Series Title S02 2013 WEB-DL 4k H265 AAC 2Audio-HDSWEB", false)]
        [InlineData("Series.Title.S02E02.This.Year.Will.Be.Different.2160p.WEB.H.265", false)]
        [InlineData("Series.Title.S02E04.German.Dubbed.DL.AAC.2160p.DV.HDR.WEB.HEVC-GROUP", false)]
        [InlineData("Series.S01.MULTi.2160p.NF.SDR.WEB.DDP.5.1.Atmos.h265-FRESH", false)]
        [InlineData("Series.Title.2016.03.14.2160p.WEB.h266-GROUP", false)]
        [InlineData("Series.Title.2016.03.14.2160p.WEB-DL.H.266-GROUP", false)]
        [InlineData("Series Title S02 2013 WEB-DL 4k H266 AAC 2Audio-GROUP", false)]
        [InlineData("Series.S01.AD.MULTi.VFF.2160p.HDR.DV.WEB.EAC3.5.1.H265-FW", false)]
        public void should_parse_webdl2160p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBDL2160p, proper);
        }

        [Theory]
        [InlineData("Series S01E01.2160P AMZN WEBRIP DD2.0 HI10P X264-TROLLUHD", false)]
        [InlineData("JUST ADD SONARR S01E01.2160P AMZN WEBRIP DD2.0 X264-TROLLUHD", false)]
        [InlineData("The.Man.In.The.Series.S01E01.2160p.AMZN.WEBRip.DD2.0.Hi10p.X264-TrollUHD", false)]
        [InlineData("The Man In the Series S01E01 2160p AMZN WEBRip DD2.0 Hi10P x264-TrollUHD", false)]
        [InlineData("House.of.Sonarr.AK.S05E08.Chapter.60.2160p.NF.WEBRip.DD5.1.x264-NTb.NLsubs", false)]
        [InlineData("Sonarr Saves the World S01 2160p Netflix WEBRip DD5.1 x264-TrollUHD", false)]
        public void should_parse_webrip2160p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.WEBRip2160p, proper);
        }

        [Theory]
        [InlineData("SERIES.S03E01-06.DUAL.Bluray.AC3.-HELLYWOOD.avi", false)]
        [InlineData("Series - S01E03 - Come Fly With Me - 720p BluRay.mkv", false)]
        [InlineData("The Big Series.S03E01.The Sonarr Can Opener.m2ts", false)]
        [InlineData("Series.S01E02.Chained.Sonarr.[Bluray720p].mkv", false)]
        [InlineData("[FFF] DATE A Sonarr Dev - 01 [BD][720p-AAC][0601BED4]", false)]
        [InlineData("[coldhell] Series v3 [BD720p][03192D4C]", false)]
        [InlineData("[RandomRemux] Series - 01 [720p BD][043EA407].mkv", false)]
        [InlineData("[Kaylith] Series Friends Specials - 01 [BD 720p AAC][B7EEE164].mkv", false)]
        [InlineData("SERIES.S03E01-06.DUAL.Blu-ray.AC3.-HELLYWOOD.avi", false)]
        [InlineData("SERIES.S03E01-06.DUAL.720p.Blu-ray.AC3.-HELLYWOOD.avi", false)]
        [InlineData("[Elysium]Lucky.Series.01(BD.720p.AAC.DA)[0BB96AD8].mkv", false)]
        [InlineData("Series.Galaxy.S01E01.33.720p.HDDVD.x264-SiNNERS.mkv", false)]
        [InlineData("The.Series.S01E07.RERIP.720p.BluRay.x264-DEMAND", true)]
        [InlineData("Sans.Series.De.Traces.FRENCH.720p.BluRay.x264-FHD", false)]
        [InlineData("Series.Black.1x01.Selezione.Naturale.ITA.720p.BDMux.x264-NovaRip", false)]
        [InlineData("Series.Hunter.S02.720p.Blu-ray.Remux.AVC.FLAC.2.0-SiCFoI", false)]
        [InlineData("Adventures.of.Sonic.the.Hedgehog.S01E01.Best.Hedgehog.720p.DD.2.0.AVC.REMUX-FraMeSToR", false)]
        public void should_parse_bluray720p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Bluray720p, proper);
        }

        [Theory]
        [InlineData("Series - S01E03 - Come Fly With Me - 1080p BluRay.mkv", false)]
        [InlineData("Sonarr.Of.Series.S02E13.1080p.BluRay.x264-AVCDVD", false)]
        [InlineData("Series.S01E02.Chained.Heat.[Bluray1080p].mkv", false)]
        [InlineData("[FFF] Series no Muromi-san - 10 [BD][1080p-FLAC][0C4091AF]", false)]
        [InlineData("[coldhell] Series v2 [BD1080p][5A45EABE].mkv", false)]
        [InlineData("[Kaylith] Series Friends Specials - 01 [BD 1080p FLAC][429FD8C7].mkv", false)]
        [InlineData("[Zurako] Log Series - 01 - The Sonarr (BD 1080p AAC) [7AE12174].mkv", false)]
        [InlineData("SERIES.S03E01-06.DUAL.1080p.Blu-ray.AC3.-HELLYWOOD.avi", false)]
        [InlineData("[Coalgirls]_Series!!_01_(1920x1080_Blu-ray_FLAC)_[8370CB8F].mkv", false)]
        [InlineData("Planet.Series.S01E11.Code.Deep.1080p.HD-DVD.DD.VC1-TRB", false)]
        [InlineData("Series Away(2001) Bluray FHD Hi10P.mkv", false)]
        [InlineData("S for Series 2005 1080p UHD BluRay DD+7.1 x264-LoRD.mkv", false)]
        [InlineData("Series.Title.2011.1080p.UHD.BluRay.DD5.1.HDR.x265-CtrlHD.mkv", false)]
        [InlineData("Fall.Of.The.Release.Groups.S02E13.1080p.BDLight.x265-AVCDVD", false)]
        [InlineData("Series Title S01E01 1080p BluRay DD 5 1 Atmos x266-GROUP", false)]
        public void should_parse_bluray1080p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Bluray1080p, proper);
        }

        [Theory]
        [InlineData("Series!!! on ICE - S01E12[JP BD Remux][ENG subs]", false)]
        [InlineData("Series.Title.S01E08.The.Well.BluRay.1080p.AVC.DTS-HD.MA.5.1.REMUX-FraMeSToR", false)]
        [InlineData("Series.Title.2x11.Nato.Per.La.Truffa.Bluray.Remux.AVC.1080p.AC3.ITA", false)]
        [InlineData("Series.Title.2x11.Nato.Per.La.Truffa.Bluray.Remux.AVC.AC3.ITA", false)]
        [InlineData("Series.Title.S03E01.The.Calm.1080p.DTS-HD.MA.5.1.AVC.REMUX-FraMeSToR", false)]
        [InlineData("Series Title Season 2 (BDRemux 1080p HEVC FLAC) [Netaro]", false)]
        [InlineData("[Vodes] Series Title - Other Title (2020) [BDRemux 1080p HEVC Dual-Audio]", false)]
        [InlineData("Adventures.of.Sonic.the.Hedgehog.S01E01.Best.Hedgehog.1080p.DD.2.0.AVC.REMUX-FraMeSToR", false)]
        [InlineData("Series Title S01 2018 1080p BluRay Hybrid-REMUX AVC TRUEHD 5.1 Dual Audio-ZR-", false)]
        [InlineData("Series.Title.S01.2018.1080p.BluRay.Hybrid-REMUX.AVC.TRUEHD.5.1.Dual.Audio-ZR-", false)]
        public void should_parse_bluray1080p_remux_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Remux1080p, proper);
        }

        [Theory]
        [InlineData("Series.Title.US.s05e13.4K.UHD.Bluray", false)]
        [InlineData("Series.Title.US.s05e13.UHD.4K.Bluray", false)]
        [InlineData("[DameDesuYo] Series Bundle - Part 1 (BD 4K 8bit FLAC)", false)]
        [InlineData("Series.Title.2014.2160p.UHD.BluRay.X265-IAMABLE.mkv", false)]
        [InlineData("Series.Title.S05EO1.Episode.Title.2160p.BDRip.AAC.7.1.HDR10.x265.10bit-Markll", false)]
        [InlineData("Series Title S01E01 2160p UHD BluRay H266-GROUP", false)]
        public void should_parse_bluray2160p_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Bluray2160p, proper);
        }

        [Theory]
        [InlineData("Series!!! on ICE - S01E12[JP BD 2160p Remux][ENG subs]", false)]
        [InlineData("Series.Title.S01E08.The.Sonarr.BluRay.2160p.AVC.DTS-HD.MA.5.1.REMUX-FraMeSToR", false)]
        [InlineData("Series.Title.2x11.Nato.Per.The.Sonarr.Bluray.Remux.AVC.2160p.AC3.ITA", false)]
        [InlineData("[Dolby Vision] Sonarr.of.Series.S07.MULTi.UHD.BLURAY.REMUX.DV-NoTag", false)]
        [InlineData("Adventures.of.Sonic.the.Hedgehog.S01E01.Best.Hedgehog.2160p.DD.2.0.AVC.REMUX-FraMeSToR", false)]
        [InlineData("Series Title S01 2018 2160p BluRay Hybrid-REMUX AVC TRUEHD 5.1 Dual Audio-ZR-", false)]
        [InlineData("Series.Title.S01.2018.2160p.BluRay.Hybrid-REMUX.AVC.TRUEHD.5.1.Dual.Audio-ZR-", false)]
        public void should_parse_bluray2160p_remux_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Remux2160p, proper);
        }

        [Theory]
        [InlineData("POI S02E11 1080i HDTV DD5.1 MPEG2-TrollHD", false)]
        [InlineData("How I Met Your Developer S01E18 Nothing Good Happens After Sonarr 720p HDTV DD5.1 MPEG2-TrollHD", false)]
        [InlineData("The Series S01E11 The Finals 1080i HDTV DD5.1 MPEG2-TrollHD", false)]
        [InlineData("Series.Title.S07E11.1080i.HDTV.DD5.1.MPEG2-NTb.ts", false)]
        [InlineData("Game of Series S04E10 1080i HDTV MPEG2 DD5.1-CtrlHD.ts", false)]
        [InlineData("Series.Title.S02E05.1080i.HDTV.DD2.0.MPEG2-NTb.ts", false)]
        [InlineData("Show - S03E01 - Episode Title Raw-HD.ts", false)]
        [InlineData("Series.Title.S10E09.Title.1080i.UPSCALE.HDTV.DD5.1.MPEG2-zebra", false)]
        [InlineData("Series.Title.2011-08-04.1080i.HDTV.MPEG-2-CtrlHD", false)]
        public void should_parse_raw_quality(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.RAWHD, proper);
        }

        [Theory]
        [InlineData("The.Series.S02E15", false)]
        [InlineData("Series.Title - 11x11 - Title", false)]
        [InlineData("Series.Title.S01E01.webm", false)]
        [InlineData("Series.Title.S01E01.The.Web.MT-dd", false)]
        public void quality_parse(string title, bool proper)
        {
            ParseAndVerifyQuality(title, Quality.Unknown, proper);
        }

        [Theory]
        [MemberData(nameof(SelfQualityParserCases))]
        public void parsing_our_own_quality_enum_name(Quality quality)
        {
            var fileName = string.Format("My series S01E01 [{0}]", SonarrQualityName(quality));
            var result = QualityParser.ParseQuality(fileName);
            result.Quality.Should().Be(quality);
        }

        [Theory]
        [MemberData(nameof(OtherSourceQualityParserCases))]
        public void should_parse_quality_from_other_source(string qualityString, Quality quality)
        {
            foreach (var c in new char[] { '-', '.', ' ', '_' })
            {
                var title = string.Format("My series S01E01 {0}", qualityString.Replace(' ', c));

                ParseAndVerifyQuality(title, quality, false);
            }
        }

        [Theory]
        [InlineData("Series - S01E01 - Title [HDTV-1080p]")]
        [InlineData("Series.Title.S10E09.Episode.Title.1080i.UPSCALE.HDTV.DD5.1.MPEG2-zebra")]
        [InlineData("Series.Title.S01E01.Bluray720p")]
        [InlineData("Series.Title.S01E01.Bluray1080p")]
        [InlineData("Series.Title.S01E01.Bluray2160p")]
        [InlineData("Series.Title.S01E01.848x480.dvd")]
        [InlineData("Series.Title.S01E01.848x480.Bluray")]
        [InlineData("Series.Title.S01E01.1280x720.Bluray")]
        [InlineData("Series.Title.S01E01.1920x1080.Bluray")]
        public void should_parse_full_quality_from_name(string title)
        {
            var result = QualityParser.ParseQuality(title);

            result.SourceDetectionSource.Should().Be(QualityDetectionSource.Name);
            result.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Name);
        }

        [Theory]
        [InlineData("[CR] Series Title - 004 [480p][48CE2D0F]")]
        [InlineData("Series.Title.S01E01.848x480")]
        [InlineData("Series.Title.S01E01.1280x720")]
        [InlineData("Series.Title.S01E01.1920x1080")]
        public void should_parse_resolution_from_name(string title)
        {
            var result = QualityParser.ParseQuality(title);

            result.SourceDetectionSource.Should().Be(QualityDetectionSource.Unknown);
            result.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Name);
        }

        [Theory]
        [InlineData("Series.Title.2011.S02E01.WS.PDTV.x264-REPACK-TLA")]
        [InlineData("Series.Title.S01E01.Bluray")]
        [InlineData("Series.Title.S01E01.HD.TV")]
        [InlineData("Series.Title.S01E01.SD.TV")]
        public void should_parse_source_from_name(string title)
        {
            var result = QualityParser.ParseQuality(title);

            result.SourceDetectionSource.Should().Be(QualityDetectionSource.Name);
            result.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Unknown);
        }

        [Theory]
        [InlineData("Series.Title.S01E02.Chained.Heat.mkv")]
        [InlineData("Series - S01E01 - Title.avi")]
        [InlineData("Series.Title.9x18.sunshine_days.avi")]
        [InlineData("[CR] Series Title - 004 [48CE2D0F].avi")]
        public void should_parse_quality_from_extension(string title)
        {
            var result = QualityParser.ParseQuality(title);

            result.SourceDetectionSource.Should().Be(QualityDetectionSource.Extension);
            result.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Extension);
        }

        [Theory]
        [InlineData("Series.Title.S01E02.Chained.Heat.1080p.mkv")]
        [InlineData("Series - S01E01 - Title.720p.avi")]
        public void should_parse_resolution_from_name_and_source_from_extension(string title)
        {
            var result = QualityParser.ParseQuality(title);

            result.SourceDetectionSource.Should().Be(QualityDetectionSource.Extension);
            result.ResolutionDetectionSource.Should().Be(QualityDetectionSource.Name);
        }

        [Theory]
        [InlineData("Series Title S04E87 REPACK 720p HDTV x264 aAF", true, 2)]
        [InlineData("Series.Title.S04E87.REPACK.720p.HDTV.x264-aAF", true, 2)]
        [InlineData("Series.Title.S04E87.REPACK2.720p.HDTV.x264-aAF", true, 3)]
        [InlineData("Series.Title.S04E87.PROPER.720p.HDTV.x264-aAF", false, 2)]
        [InlineData("Series.Title.S01E07.RERIP.720p.BluRay.x264-DEMAND", true, 2)]
        [InlineData("Series.Title.S01E07.RERIP2.720p.BluRay.x264-DEMAND", true, 3)]
        public void should_be_able_to_parse_repack(string title, bool isRepack, int version)
        {
            var result = QualityParser.ParseQuality(title);
            result.Revision.Version.Should().Be(version);
            result.Revision.IsRepack.Should().Be(isRepack);
        }

        [Theory]
        [InlineData("[MTBB] Series Title - S02E02 - 027 - Episode Title [WEBDL-1080p v2][x264][AAC]", 2)]
        public void should_be_able_to_parse_anime_version(string title, int version)
        {
            var result = QualityParser.ParseQuality(title);
            result.Revision.Version.Should().Be(version);
        }

        private void ParseAndVerifyQuality(string title, Quality quality, bool proper)
        {
            var result = QualityParser.ParseQuality(title);
            result.Quality.Should().Be(quality);

            var version = proper ? 2 : 1;
            result.Revision.Version.Should().Be(version);
        }
    }
}
