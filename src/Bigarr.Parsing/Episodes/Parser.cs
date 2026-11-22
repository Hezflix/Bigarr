// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/Parser.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bigarr.Parsing.Internal;
using Bigarr.Parsing.Episodes.Model;

namespace Bigarr.Parsing.Episodes
{
    public static partial class Parser
    {
        // Bigarr: was Episode.AIR_DATE_FORMAT (NzbDrone.Core.Tv), a constant of the series model.
        private const string AirDateFormat = "yyyy-MM-dd";

        private static readonly ParserLog Logger = ParserLog.For(typeof(Parser));

        private static readonly Regex[] ReportTitleRegex = new[]
            {
                // Anime - Absolute Episode Number + Title + Season+Episode
                // Todo: This currently breaks series that start with numbers
//                new Regex(@"^(?:(?<absoluteepisode>\d{2,3})(?:_|-|\s|\.)+)+(?<title>.+?)(?:\W|_)+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+)",
//                          RegexOptions.IgnoreCase | RegexOptions.Compiled),

                // Daily episode with year in series title and air time after date (Plex DVR format)
                ReportTitleRegex1,

                // Daily episodes without title (2018-10-12, 20181012) (Strict pattern to avoid false matches)
                ReportTitleRegex2,

                // Multi-Part episodes without a title (S01E05.S01E06)
                ReportTitleRegex3,

                // Multi-Part episodes without a title (1x05.1x06)
                ReportTitleRegex4,

                // Episodes without a title, Multi (S01E04E05, 1x04x05, etc)
                ReportTitleRegex5,

                // Split episodes (S01E05a, S01E05b, etc)
                ReportTitleRegex6,

                // Episodes without a title, Single (S01E05, 1x05)
                ReportTitleRegex7,

                // Anime - [SubGroup] Title Absolute (Season+Episode)
                ReportTitleRegex8,

                // Anime - [SubGroup] Title Absolute - Season+Episode
                ReportTitleRegex9,

                // Anime - [SubGroup] Title Season+Episode
                ReportTitleRegex10,

                // Anime - [SubGroup] Title Episode Absolute Episode Number ([SubGroup] Series Title Episode 01)
                ReportTitleRegex11,

                // Anime - [SubGroup] Title Absolute Episode Number + Season+Episode
                ReportTitleRegex12,

                // Anime - [SubGroup] Title Multi-episode + optional Absolute Episode Number
                ReportTitleRegex13,

                // Anime - [SubGroup] Title Single episode + Absolute Episode Number
                ReportTitleRegex14,

                // Anime - [SubGroup] Title with trailing number Absolute Episode Number - Batch separated with tilde
                ReportTitleRegex15,

                // Anime - [SubGroup] Title with season number in brackets Absolute Episode Number
                ReportTitleRegex16,

                // Anime - [SubGroup] Title with trailing 3-digit number and sub title - Absolute Episode Number
                ReportTitleRegex17,

                // Anime - [SubGroup] Multi-season pack
                ReportTitleRegex18,

                // Anime - [SubGroup] Partial season pack
                ReportTitleRegex19,

                // Anime - [SubGroup] Title with trailing number Absolute Episode Number
                ReportTitleRegex20,

                // Anime - [SubGroup] Title with trailing number S## (Full season)
                ReportTitleRegex21,

                // Anime - [SubGroup] Title with trailing number Absolute Episode Number
                ReportTitleRegex22,

                // Anime - [SubGroup] Title - Absolute Episode Number
                ReportTitleRegex23,

                // Anime - [SubGroup] Title Absolute Episode Number - Absolute Episode Number (batches without full separator between title and absolute episode numbers)
                ReportTitleRegex24,

                // Anime - [SubGroup] Title (Season only)
                ReportTitleRegex25,

                // Anime - [SubGroup] Title Absolute Episode Number
                ReportTitleRegex26,

                // Multi-episode Repeated (S01E05 - S01E06)
                ReportTitleRegex27,

                // Multi-episode Repeated (1x05 - 1x06)
                ReportTitleRegex28,

                // Single episodes with a title (S01E05, 1x05, etc) followed by ".5 [SP]"
                ReportTitleRegex29,

                // Single episodes with a title (S01E05, 1x05, etc) and trailing info in slashes
                ReportTitleRegex30,

                // Anime - Title Season EpisodeNumber + Absolute Episode Number [SubGroup]
                ReportTitleRegex31,

                // Multi-Episode with a title (S01E05E06, S01E05-06, S01E05 E06, etc) and trailing info in slashes
                ReportTitleRegex32,

                // Anime - Title Absolute Episode Number [SubGroup] [Hash]? (Series Title Episode 99-100 [RlsGroup] [ABCD1234])
                ReportTitleRegex33,

                // Anime - Title Absolute Episode Number [SubGroup] [Hash]
                ReportTitleRegex34,

                // Anime - Title Absolute Episode Number (Year) [SubGroup]
                ReportTitleRegex35,

                // Anime - Title with trailing number, Absolute Episode Number and hash
                ReportTitleRegex36,

                // Anime - Title Absolute Episode Number [Hash]
                ReportTitleRegex37,

                // Episodes with airdate AND season/episode number, capture season/episode only
                ReportTitleRegex38,

                // Episodes with airdate AND season/episode number
                ReportTitleRegex39,

                // Episodes with absolute episode number AND airdate (TJET Wrestling)
                ReportTitleRegex40,

                // Single or multi episode releases with multiple titles, then season and episode numbers after the last title. (Title1 / Title2 / ... / S1E1-2 of 6)
                ReportTitleRegex41,

                // Multi-episode with title (S01E99-100, S01E05-06)
                ReportTitleRegex42,

                // Multi-episode with title (S01E05-06, S01E05-6)
                ReportTitleRegex43,

                // Episodes with a Chinese title and English title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
                ReportTitleRegex44,

                // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
                ReportTitleRegex45,

                // Episodes with a title, 4 digit season number, Single episodes (S2016E05, etc) & Multi-episode (S2016E05E06, S2016E05-06, S2016E05 E06, etc)
                ReportTitleRegex46,

                // Episodes with a title, 4 digit season number, Single episodes (2016x05, etc) & Multi-episode (2016x05x06, 2016x05-06, 2016x05 x06, etc)
                ReportTitleRegex47,

                // Episodes with a title, Single episodes (s01.05)
                ReportTitleRegex48,

                // Multi-season pack
                ReportTitleRegex49,

                // Partial season pack
                ReportTitleRegex50,

                // Anime - 4 digit absolute episode number in batch (1017-1088) or 1017-1088
                ReportTitleRegex51,

                // Anime - 4 digit absolute episode number
                ReportTitleRegex52,

                // Mini-Series, treated as season 1, multi episodes are labelled as E1-E2
                ReportTitleRegex53,

                // 5 digit episode number with a title
                ReportTitleRegex54,

                // 5 digit multi-episode with a title
                ReportTitleRegex55,

                // Episodes with airdate and part (2018.04.28.Part.2)
                ReportTitleRegex56,

                // Episodes with airdate (2018.04.28)
                ReportTitleRegex57,

                // Anime - Title 4-digit Absolute Episode Number [SubGroup]
                ReportTitleRegex58,

                // Mini-Series with year in title, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
                ReportTitleRegex59,

                // 3-digit season number
                // Single episode: Title.S001E01
                // Multi episode: Title.S001E01-E02
                ReportTitleRegex60,

                // Mini-Series, treated as season 1, episodes are labelled as XofY
                ReportTitleRegex61,

                // Supports Season 01 Episode 03
                ReportTitleRegex62,

                // Multi-episode with episodes in square brackets (Series Title [S01E11E12] or Series Title [S01E11-12])
                ReportTitleRegex63,

                // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S01E11-12) or Series Title (S01E1-3 of 12))
                ReportTitleRegex64,

                // Multi-episode release with no space between series title and season (S01E11E12)
                ReportTitleRegex65,

                // Multi-episode with single episode numbers (S6.E1-E2, S6.E1E2, S6E1E2, etc)
                ReportTitleRegex66,

                // Single or multi episode releases with multiple titles, each followed by season and episode numbers in brackets
                ReportTitleRegex67,

                // Multi-episode with episodes in brackets (Series Title (S01E11E12) or Series Title (S1E11-12) or Series Title (S1E1-3 of 12))
                ReportTitleRegex68,

                // Single episode season or episode S1E1 or S1-E1 or S1.Ep1 or S01.Ep.01
                ReportTitleRegex69,

                // 3 digit season S010E05
                ReportTitleRegex70,

                // Separated season and episode numbers S01 - E01
                ReportTitleRegex71,

                // Season and episode numbers in square brackets (single and multi-episode)
                // Series Title - [02x01] - Episode 1
                // Series Title - [02x01x02] - Episode 1
                ReportTitleRegex72,

                // Anime - Title with season number - Absolute Episode Number (Title S01 - EP14)
                ReportTitleRegex73,

                // Anime - French titles with single episode numbers, with or without leading sub group ([RlsGroup] Title - Episode 1)
                ReportTitleRegex74,

                // Anime - Absolute episode number in square brackets
                ReportTitleRegex75,

                // Japanese variety shows with leading date
                ReportTitleRegex76,

                // Season only releases followed by year
                ReportTitleRegex77,

                // Season only releases
                ReportTitleRegex78,

                // 4 digit season only releases
                ReportTitleRegex79,

                // Spanish tracker releases
                ReportTitleRegex80,

                // Supports 103/113 naming
                ReportTitleRegex81,

                // 4 digit episode number
                // Episodes without a title, Single (S01E05, 1x05) AND Multi (S01E04E05, 1x04x05, etc)
                ReportTitleRegex82,

                // 4 digit episode number
                // Episodes with a title, Single episodes (S01E05, 1x05, etc) & Multi-episode (S01E05E06, S01E05-06, S01E05 E06, etc)
                ReportTitleRegex83,

                // Turkish tracker releases (01 BLM, 3. Blm, 04.Bolum, etc)
                ReportTitleRegex84,

                // Episodes with airdate (04.28.2018)
                ReportTitleRegex85,

                // Episodes with airdate (28.04.2018)
                ReportTitleRegex86,

                // Episodes with airdate (20180428)
                ReportTitleRegex87,

                // Anime - Title Absolute Episode Number (E195 or E1206)
                ReportTitleRegex88,

                // Daily episodes that use short month format instead of number
                ReportTitleRegex89,

                // Supports 1103/1113 naming
                ReportTitleRegex90,

                // Dutch/Flemish release titles
                ReportTitleRegex91,

                // Episodes with single digit episode number (S01E1, S01E5E6, etc)
                ReportTitleRegex92,

                // iTunes Season 1\05 Title (Quality).ext
                ReportTitleRegex93,

                // iTunes 1-05 Title (Quality).ext
                ReportTitleRegex94,

                // Mini-Series, treated as season 1, episodes are labelled as Part01, Part 01, Part.1
                ReportTitleRegex95,

                // Mini-Series, treated as season 1, episodes are labelled as Part One/Two/Three/...Nine, Part.One, Part_One
                ReportTitleRegex96,

                // Anime Range - Title Absolute Episode Number (ep01-12)
                ReportTitleRegex97,

                // Anime - Title Absolute Episode Number (e66)
                ReportTitleRegex98,

                // Anime - Title Episode Absolute Episode Number (Series Title Episode 01)
                ReportTitleRegex99,

                // Anime Range - Title Absolute Episode Number (1 or 2 digit absolute episode numbers in a range, 1-10)
                ReportTitleRegex100,

                // Anime - Title Episode/Episodio Absolute Episode Number
                ReportTitleRegex101,

                // Anime - Title [Absolute Episode Number] from AniLibriaTV
                ReportTitleRegex102,

                // Simple season releases in the format 'Series (year) - Temporada season'
                ReportTitleRegex103,

                // Anime - Title Absolute Episode Number
                ReportTitleRegex104,

                // Anime - Title {Absolute Episode Number}
                ReportTitleRegex105,

                // Extant, terrible multi-episode naming (extant.10708.hdtv-lol.mp4)
                ReportTitleRegex106,

                // Episodes without a title, Single episode numbers (S1E1, 1x1)
                ReportTitleRegex107,

                // Anime OVA special
                ReportTitleRegex108,

                // Episodes with a title, 3 digit season number, Single episodes (S001E05, 1x05, etc) & Multi-episode (S001E05E06, S001E05-06, S001E05 E06, etc)
                ReportTitleRegex109,
            };

        [GeneratedRegex(@"^^(?<title>.+?\((?<titleyear>\d{4})\))[-_. ]+(?<airyear>19[4-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])[-_. ]\d{2}[-_. ]\d{2}[-_. ]\d{2}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex1 { get; }

        [GeneratedRegex(@"^(?<airyear>19[6-9]\d|20\d\d)(?<sep>[-_]?)(?<airmonth>0\d|1[0-2])\k<sep>(?<airday>[0-2]\d|3[01])(?!\d)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex2 { get; }

        [GeneratedRegex(@"^(?:\W*S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:e{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex3 { get; }

        [GeneratedRegex(@"^(?:\W*(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex4 { get; }

        [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_]|[ex]){1,2}(?<episode>\d{2,3}(?!\d+))){2,})", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex5 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))(?<splitepisode>[a-d])(?:[ _.])))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex6 { get; }

        [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{2,3}(?!\d+))))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex7 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ])+\((?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:\)(?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex8 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ](?<![()\[!]))+(?:S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex9 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+)(?:v\d+)?(?:[_. ](?!\d+)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex10 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex11 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?))+(?:_|-|\s|\.)+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+)))+).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex12 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))){2,})(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))*.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex13 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\](?:_|-|\s|\.)?)(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]){1,2}(?<episode>\d{2}(?!\d+))))(?:(?:_|-|\s|\.)+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|\-[a-z])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex14 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\s?~\s?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex15 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)[_. ]+?\(Season[_. ](?<season>\d+)\)[-_. ]+?(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex16 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>[^\]]+?)\][-_. ]?(?<title>[^]]+?)(?:[-_. ]{3}?(?<absoluteepisode>\d{2}(\.\d{1,2})?(?!-?\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex17 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:-S?|[-_. ]{1,3}S)(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex18 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex19 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex20 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+(?:S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?![ex]?\d+))).+?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex21 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+)[_ ]+)(?:[-_. ]?(?<absoluteepisode>\d{3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex22 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?:[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-])))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex23 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:(?<!\b[0]\d+))(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))[. ]-[. ](?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex24 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ])?(?<title>.+?)[-_. ]+?[\[(](?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. )\]]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex25 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)[-_. ]+\(?(?:[-_. ]?#?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|-[a-z]+)))+\)?(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex26 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:e|[-_. ]e){1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex27 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:x{1,2}(?<episode>\d{1,3}(?!\d+)))+){2,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex28 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))(?<special>\.5[ .]\[SP\]))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex29 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+|(?:[ex]|\W[ex]|_|-){1,2}\d+))).+?(?:\[.+?\])(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex30 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:[ex]|\W[ex]|-){1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+)[-_. (]+?(?:[-_. ]?(?<absoluteepisode>(?<!\d+)\d{3}(\.\d{1,2})?(?!\d+|[pi])))+.+?\[(?<subgroup>.+?)\](?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex31 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))+).+?(?:\[.+?\])(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex32 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]Episode(?:[-_. ]+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex33 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{3}(\.\d{1,2})(?!\d+)))+(?:.+?)\[(?<subgroup>.+?)\].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex34 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2}(?!\d+))[-_. ](\(\d{4}\))[-_. ]\[(?<subgroup>.+?)\]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex35 { get; }

        [GeneratedRegex(@"^(?<title>[^-]+?)(?:(?<![-_. ]|\b[0]\d+) - )(?:[-_. ]?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?.*?(?<hash>[(\[][0-9A-F]{8}[)\]])(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex36 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:_|-|\s|\.)+(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+)))+(?:[-_. ]+(?<special>special|ova|ovd))?[-_. ]+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex37 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airdate>\d{4}\W+[0-1][0-9]\W+[0-3][0-9])(?!\W+[0-3][0-9])[-_. ](?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex38 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:s?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+)))(?:[ex](?<episode>(?<!\d+)(?:\d{1,3})(?!\d+)))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex39 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?[-_. ](?:e\d{2,3}(?!\d+))[-_. ](?<airyear>\d{4})\W+(?<airmonth>[0-1][0-9])\W+(?<airday>[0-3][0-9])(?!\W+[0-3][0-9]).+?(?:\[[a-z]+\])", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex40 { get; }

        [GeneratedRegex(@"^((?<title>.*?)[ ._]\/[ ._])+\(?S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?(?:[ ._]of[ ._](?<episodecount>\d{1,2}))?\)?[ ._][\(\[]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex41 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{2,3}(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+)))+(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex42 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))E(?<episode>\d{1,2}(?!\d+))(?:-(?<episode>\d{1,2}(?!\d+)))+(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex43 { get; }

        [GeneratedRegex(@"^(?<title>[\u4E00-\u9FCC]+)[_. ](?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex44 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex45 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:e|\We|_){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|e|\We|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex46 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<season>(?<!\d+)(?:\d{4})(?!\d+))(?:x|\Wx){1,2}(?<episode>\d{2,4}(?!\d+))(?:(?:\-|x|\Wx|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)\W?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex47 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:[-_\W](?<![()\[!]))+S(?<season>(?<!\d+)(?:\d{2})(?!\d+))(?:\.)(?<episode>\d{2,3}(?!\d+))(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex48 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(Complete Series)?[-_. ]+(?:S|(?:Season|Saison|Series|Stagione)[_. ])(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:(?:[-_. ]{1}|[-_. ]{3})(?:S|(?:Season|Saison|Series|Stagione)[_. ])?(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))){1,}", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex49 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:\W+S(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))\W+(?:(?:(?:Part|Vol)\W?|(?<!\d+\W+)e|p)(?<seasonpart>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex50 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\(?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))[-](?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))\)?", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex51 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?(?<absoluteepisode>\d{4}(\.\d{1,2})?(?!\d+))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex52 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:[-._ ][e])(?<episode>\d{2,3}(?!\d+))(?:(?:\-?[e])(?<episode>\d{2,3}(?!\d+)))+", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex53 { get; }

        [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex54 { get; }

        [GeneratedRegex(@"^(?:(?<title>.+?)(?:_|-|\s|\.)+)(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:(?:[-_. ]{1,3}ep){1,2}(?<episode>(?<!\d+)\d{5}(?!\d+)))+", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex55 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ]+Part[-_. ]?(?<part>[1-9])", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex56 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<airyear>\d{4})[-_. ]+(?<airmonth>[0-1][0-9])[-_. ]+(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex57 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<absoluteepisode>(?<!\d+)\d{4}(?!\d+))[-_. ]\[(?<subgroup>.+?)\]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex58 { get; }

        [GeneratedRegex(@"^(?<title>.+?\d{4})(?:\W+(?:(?:Part\W?|e)(?<episode>\d{1,2}(?!\d+)))+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex59 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+S(?<season>(?<!\d+)\d{3}(?!\d+))E(?<episode>(?<!\d+)\d{2}(?!\d+))(?:-[ex](?<episode>(?<!\d+)\d{2}(?!\d+)))?([_. ]|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex60 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?<episode>(?<!\d+)\d{1,2}(?!\d+))of\d+)+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex61 { get; }

        [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:[-_\W](?<![()\[]))+(?:\W?Season\W?)(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)+(?:Episode\W)(?:[-_. ]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex62 { get; }

        [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\[S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{2}(?!\d+)))+\]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex63 { get; }

        [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{2}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex64 { get; }

        [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)S(?<season>(?<!\d+)\d{2}(?!\d+))(?:E(?<episode>(?<!\d+)\d{2}(?!\d+)))+", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex65 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]S(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:[-_. ]?[ex]?(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex66 { get; }

        [GeneratedRegex(@"^(?<title>.*?)[ ._]\(S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?E?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))(?:-(?<episode>(?<!\d+)\d{1,2}(?!\d+)))?\)(?:[ ._]\/[ ._])(?<title>.*?)[ ._]\(", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex67 { get; }

        [GeneratedRegex(@"(?:.*(?:^))(?<title>.*?)[-._ ]+\(S(?<season>(?<!\d+)\d{1}(?!\d+))(?:[E-]{1,2}(?<episode>(?<!\d+)\d{1,2}(?!\d+)))+([._ ]+of[._ ]+[\dx]+)?\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex68 { get; }

        [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:\W|_)?Ep?[ ._]?(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex69 { get; }

        [GeneratedRegex(@"(?:.*(?:\""|^))(?<title>.*?)(?:\W?|_)S(?<season>(?<!\d+)\d{3}(?!\d+))(?:\W|_)?E(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex70 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:_|-|\s|\.)+S(?<season>\d{2}(?!\d+))(\W-\W)E(?<episode>(?<!\d+)\d{2}(?!\d+))(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex71 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?(?:[-_\W](?<![()\[!]))+\[(?:s)?(?<season>(?<!\d+)\d{1,2})(?:(?:[ex])(?<episode>\d{2}))(?:(?:[-ex]){1,2}(?<episode>\d{2}))*\].+?(?:\.|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex72 { get; }

        [GeneratedRegex(@"^(?<title>.+?S\d{1,2})[-_. ]{3,}(?:EP)?(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+|[-]))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex73 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+?(?:Episode[-_. ]+?)(?<absoluteepisode>\d{1}(\.\d{1,2})?(?!\d+))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex74 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>.+?)[-_. ]+?\[(?<absoluteepisode>\d{2,3}(\.\d{1,2})?(?!\d+))\]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex75 { get; }

        [GeneratedRegex(@"^(?<airyear>\d{2})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?![-_. ]+[0-3][0-9])[-_. ](?<title>.+?)[-_. ](?:Season[-_. ]?(?<season>\d{1,2})[-_. ])?(?:ep|#)(?<episode>\d{2,3})", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex76 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?=[-_. ]\d{4}[-_. ]+))(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex77 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{1,2}(?![-_. ]?\d+))(?:[-_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex78 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?(?:S|Season|Saison|Series|Stagione)[-_. ]?(?<season>\d{4}(?![-_. ]?\d+))(\W+|_|$)(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex79 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_. ]+?Temporada.+?|\[.+?\])\[Cap)(?:[-_. ]+(?<season>(?<!\d+)\d{1,2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])))+(?:\])", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex80 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[_.-](?<![()\[!]))+(?<season>(?<!\d+)[1-9])(?<episode>[1-9][0-9]|[0][1-9])(?![a-z]|\d+))+(?:[_.]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex81 { get; }

        [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex82 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]|\d{1,2}-))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{4}(?!\d+|i|p)))+)\W?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex83 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[_. ](?<absoluteepisode>\d{1,4})(?:[_. ]+)(?:BLM|B[oö]l[uü]m)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex84 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex85 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<ambiguousairday>[0-3][0-9])[-_. ]+(?<ambiguousairmonth>[0-1][0-9])[-_. ]+(?<airyear>\d{4})(?!\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex86 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?\W*(?<!\d+)(?<airyear>\d{4})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9])(?!\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex87 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>(\d{3}|\d{4})(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex88 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+(?<airday>[1-2]\d|3[01]|[1-9])(?:th|st|rd)[-_. ](?<shortairmonth>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[-_. ](?<airyear>19[4-9]\d|20\d\d)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex89 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_. ](?<![()\[!]))*(?<!\d{1,2}-)(?<season>(?<!\d+|\(|\[|e|x)\d{2})(?<episode>(?<!e|x)(?:[1-9][0-9]|[0][1-9])(?!p|i|\d+|\)|\]|\W\d+|\W(?:e|ep|x)\d+)))+([-_. ]+|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex90 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Se\.(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?afl\.)(?<episode>\d{1,3}(?!\d+))(?:(?:[-]|[-_ ]en[-_ ])(?<episode>\d{1,3}(?!\d+)))*))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex91 { get; }

        [GeneratedRegex(@"^(?<title>.*?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)\d{1,2}(?!\d+))(?:(?:\-|[ex]){1,2}(?<episode>\d{1}))+)+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex92 { get; }

        [GeneratedRegex(@"^(?:Season(?:_|-|\s|\.)(?<season>(?<!\d+)\d{1,2}(?!\d+)))(?:_|-|\s|\.)(?<episode>(?<!\d+)\d{1,2}(?!\d+))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex93 { get; }

        [GeneratedRegex(@"^(?:(?<season>(?<!\d+)(?:\d{1,2})(?!\d+))(?:-(?<episode>\d{2,3}(?!\d+))))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex94 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:(?:(?<!\()Part\W?|(?<!\d+\W+)e)(?<episode>\d{1,2}(?!\d+|\))))+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex95 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:\W+(?:Part[-._ ](?<episode>One|Two|Three|Four|Five|Six|Seven|Eight|Nine)(?>[-._ ])))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex96 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:_|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,3}(\.\d{1,2})?)-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex97 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:_|-|\s|\.)+(?:e|ep)(?<absoluteepisode>\d{2,4}(\.\d{1,2})?))+[-_. ].*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex98 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ](?:Episode)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+)))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex99 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[_. ]+(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+))-(?<absoluteepisode>(?<!\d+)\d{1,2}(\.\d{1,2})?(?!\d+|-)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex100 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)[-_. ]+(?:Episode|Episodio)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex101 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]\[)(?:(?:-?)(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+(?:\][-_. ]).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex102 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[_. ]-[_. ](?:Temporada)[-_. ]?(?<season>\d{1,2}(?![_. ]?\d+))(?:[_. ]|$)+(?<extras>EXTRAS|SUBPACK)?(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex103 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:[-_. ]+(?<absoluteepisode>(?<!\d+)\d{2,4}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex104 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)?(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+(?<absoluteepisode>(?<!\d+)\d{2,3}(\.\d{1,2})?(?!\d+|[ip])))+.*?(?<hash>[(\[][0-9A-F]{8}[)\]])?$", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex105 { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ](?<season>[0]?\d?)(?:(?<episode>\d{2}){2}(?!\d+))[-_. ]", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex106 { get; }

        [GeneratedRegex(@"^(?:S?(?<season>(?<!\d+)(?:\d{1,2}|\d{4})(?!\d+))(?:(?:[-_ ]?[ex])(?<episode>\d{1}(?!\d+))))", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex107 { get; }

        [GeneratedRegex(@"^\[(?<subgroup>.+?)\][-_. ]?(?<title>.+?)(?:[-_. ]+(?<special>special|ova|ovd|ncop|nced)).*?(?<hash>[(\[][0-9A-F]{8}[)\]])?(?:$|\.mkv)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex108 { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:(?:[-_\W](?<![()\[!]))+S?(?<season>(?<!\d+)(?:\d{3})(?!\d+))(?:[ex]|\W[ex]){1,2}(?<episode>\d{2,3}(?!\d+))(?:(?:\-|[ex]|\W[ex]|_){1,2}(?<episode>\d{2,3}(?!\d+)))*)(?:[-_. ]|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTitleRegex109 { get; }

        private static readonly Regex[] SpecialEpisodeTitleRegex = new Regex[]
            {
                SpecialEpisodeTitleRegex1,

                SpecialEpisodeTitleRegex2
            };

        [GeneratedRegex(@"\.S\d+E00\.(?<episodetitle>.+?)(?:\.(?:720p|1080p|2160p|HDTV|WEB|WEBRip|WEB-DL)\.|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SpecialEpisodeTitleRegex1 { get; }

        [GeneratedRegex(@"\.S\d+\.Special\.(?<episodetitle>.+?)(?:\.(?:720p|1080p|2160p|HDTV|WEB|WEBRip|WEB-DL)\.|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SpecialEpisodeTitleRegex2 { get; }

        private static readonly Regex[] RejectHashedReleasesRegexes = new Regex[]
            {
                // Generic match for md5 and mixed-case hashes.
                RejectHashedReleasesRegexes1,

                // Generic match for shorter lower-case hashes.
                RejectHashedReleasesRegexes2,

                // Format seen on some NZBGeek releases
                // Be very strict with these coz they are very close to the valid 101 ep numbering.
                RejectHashedReleasesRegexes3,
                RejectHashedReleasesRegexes4,

                // Backup filename (Unknown origins)
                RejectHashedReleasesRegexes5,

                // 123 - Started appearing December 2014
                RejectHashedReleasesRegexes6,

                // abc - Started appearing January 2015
                RejectHashedReleasesRegexes7,

                // abc - Started appearing 2020
                RejectHashedReleasesRegexes8,

                // b00bs - Started appearing January 2015
                RejectHashedReleasesRegexes9,

                // 170424_26 - Started appearing August 2018
                new Regex(@"^\d{6}_\d{2}$"),

                // additional Generic match for mixed-case hashes. - Started appearing Dec 2020
                RejectHashedReleasesRegexes10,

                // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
                RejectHashedReleasesRegexes11,

                // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
                RejectHashedReleasesRegexes12,

                // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
                RejectHashedReleasesRegexes13,
            };

        [GeneratedRegex(@"^[0-9a-zA-Z]{32}")]
        private static partial Regex RejectHashedReleasesRegexes1 { get; }

        [GeneratedRegex(@"^[a-z0-9]{24}$")]
        private static partial Regex RejectHashedReleasesRegexes2 { get; }

        [GeneratedRegex(@"^[A-Z]{11}\d{3}$")]
        private static partial Regex RejectHashedReleasesRegexes3 { get; }

        [GeneratedRegex(@"^[a-z]{12}\d{3}$")]
        private static partial Regex RejectHashedReleasesRegexes4 { get; }

        [GeneratedRegex(@"^Backup_\d{5,}S\d{2}-\d{2}$")]
        private static partial Regex RejectHashedReleasesRegexes5 { get; }

        [GeneratedRegex(@"^123$")]
        private static partial Regex RejectHashedReleasesRegexes6 { get; }

        [GeneratedRegex(@"^abc$", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegexes7 { get; }

        [GeneratedRegex(@"^abc[-_. ]xyz", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegexes8 { get; }

        [GeneratedRegex(@"^b00bs$", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegexes9 { get; }

        [GeneratedRegex(@"^[0-9a-zA-Z]{30}")]
        private static partial Regex RejectHashedReleasesRegexes10 { get; }

        [GeneratedRegex(@"^[0-9a-zA-Z]{26}")]
        private static partial Regex RejectHashedReleasesRegexes11 { get; }

        [GeneratedRegex(@"^[0-9a-zA-Z]{39}")]
        private static partial Regex RejectHashedReleasesRegexes12 { get; }

        [GeneratedRegex(@"^[0-9a-zA-Z]{24}")]
        private static partial Regex RejectHashedReleasesRegexes13 { get; }

        private static readonly Regex[] SeasonFolderRegexes = new Regex[]
            {
                SeasonFolderRegexes1
            };

        [GeneratedRegex(@"^(Season[ ._-]*\d+|Specials)$")]
        private static partial Regex SeasonFolderRegexes1 { get; }

        // Regex to detect whether the title was reversed.
        [GeneratedRegex(@"(?:^|[-._ ])(p027|p0801|\d{2,3}E-?\d{2}S)[-._ ]")]
        private static partial Regex ReversedTitleRegex { get; }

        [GeneratedRegex(@"\[[^\[\]]+\]|\([^()]+\)|\{[^{}]+\}")]
        private static partial Regex AnimeSeasonReleaseMetadataRegex { get; }

        [GeneratedRegex(@"\b(special|ova|ovd|ncop|nced)\b", RegexOptions.IgnoreCase)]
        private static partial Regex AnimeSpecialMarkerRegex { get; }

        private static readonly RegexReplace NormalizeRegex = new RegexReplace(NormalizeRegex1, string.Empty);

        [GeneratedRegex(@"((?:\b|_)(?<!^)([aà](?!$)|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", RegexOptions.IgnoreCase)]
        private static partial Regex NormalizeRegex1 { get; }

        [GeneratedRegex(@"(?<=\b\d+)%")]
        private static partial Regex PercentRegex { get; }

        private static readonly RegexReplace SimpleTitleRegex = new RegexReplace(SimpleTitleRegex1, string.Empty);

        [GeneratedRegex(@"(?:(480|540|576|720|1080|1440|2160)[ip]|(?<![a-z0-9])[248]k(?![a-z0-9])|[xh][\W_]?26[456]|DD\W?5\W1|[<>?*]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(?<![a-f0-9])(8|10)[ -]?(b(?![a-z0-9])|bit))\s*?", RegexOptions.IgnoreCase)]
        private static partial Regex SimpleTitleRegex1 { get; }

        // Valid TLDs http://data.iana.org/TLD/tlds-alpha-by-domain.txt

        [GeneratedRegex(@"(?<=[_.-])(?<airdate>(?<!\d)(?<airyear>[1-9]\d{1})(?<airmonth>[0-1][0-9])(?<airday>[0-3][0-9]))(?=[_.-])", RegexOptions.IgnoreCase)]
        private static partial Regex SixDigitAirDateRegex { get; }

        [GeneratedRegex(@"\[[a-z0-9 ._-]+\]$", RegexOptions.IgnoreCase)]
        private static partial Regex CleanQualityBracketsRegex { get; }

        [GeneratedRegex(@"^(?<title>.+?)[-_. ]+?[\(\[]?(?<year>\d{4})[\]\)]?", RegexOptions.IgnoreCase)]
        private static partial Regex YearInTitleRegex { get; }

        [GeneratedRegex(@"^(?:(?<title>.+?) \((?<title>.+?)\)|(?<title>.+?) \| (?<title>.+?)|(?<title>.+?) AKA (?<title>.+?)|(?<title>.+?) / (?<title>.+?))$", RegexOptions.IgnoreCase)]
        private static partial Regex TitleComponentsRegex { get; }

        [GeneratedRegex(@"\(\d+\)$")]
        private static partial Regex PartRegex { get; }
        [GeneratedRegex(@"[^\w\s]")]
        private static partial Regex PunctuationRegex { get; }
        [GeneratedRegex(@"^(a|an|the)\s", RegexOptions.IgnoreCase)]
        private static partial Regex ArticleWordRegex { get; }
        [GeneratedRegex(@"\b(part|special|edition|christmas)\b\s?", RegexOptions.IgnoreCase)]
        private static partial Regex SpecialEpisodeWordRegex { get; }
        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex DuplicateSpacesRegex { get; }
        [GeneratedRegex(@"^(?:S|Season|Saison|Series|Stagione|Sezon)[-_. ]*(?<season>(?<!\d+)\d{1,4}(?!\d+))(?:[_. ]+(?!\d+)|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SeasonFolderRegex { get; }
        [GeneratedRegex(@"^[ex]?(?<episode>(?<!\d+)\d{1,3}(?!\d+))(?:[ex-](?<episode>(?<!\d+)\d{1,3}(?!\d+)))?(?:[_. ](?!\d+)(?<remaining>.+)|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SimpleEpisodeNumberRegex { get; }

        [GeneratedRegex(@"^(?:\[.+?\])+")]
        private static partial Regex RequestInfoRegex { get; }

        private static readonly string[] Numbers = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        [GeneratedRegex(@"[-_. \[](?<multi>multi|multilang|multilanguage)[-_. \]]", RegexOptions.IgnoreCase)]
        private static partial Regex MultiRegex { get; }

        private static readonly Dictionary<string, int> ShortMonths = new()
        {
            { "jan", 1 },
            { "feb", 2 },
            { "mar", 3 },
            { "apr", 4 },
            { "may", 5 },
            { "jun", 6 },
            { "jul", 7 },
            { "aug", 8 },
            { "sep", 9 },
            { "oct", 10 },
            { "nov", 11 },
            { "dec", 12 },
        };

        public static ParsedEpisodeInfo ParsePath(string path)
        {
            var fileInfo = new FileInfo(path);
            var fileName = fileInfo.Name;

            foreach (var replace in ParserCommon.PathPreSubstitutionRegex)
            {
                if (replace.TryReplace(ref fileName))
                {
                    Logger.Trace($"Replace regex: {replace}");
                    Logger.Debug("Substituted with " + fileName);
                }
            }

            var result = ParseTitle(fileName);

            // Parse using the folder and file separately, but combine if they both parse correctly.
            var episodeNumberMatch = SimpleEpisodeNumberRegex.Match(fileName);

            if (episodeNumberMatch.Success && fileInfo.Directory?.Name != null && (result == null || result.IsMiniSeries || result.AbsoluteEpisodeNumbers.Any()))
            {
                var seasonMatch = SeasonFolderRegex.Match(fileInfo.Directory.Name);

                if (seasonMatch.Success && seasonMatch.Groups["season"].Success)
                {
                    var episodeCaptures = episodeNumberMatch.Groups["episode"].Captures.Cast<Capture>().ToList();
                    var first = ParseNumber(episodeCaptures.First().Value);
                    var last = ParseNumber(episodeCaptures.Last().Value);
                    var pathTitle = $"S{seasonMatch.Groups["season"].Value}E{first:00}";

                    if (first != last)
                    {
                        pathTitle += $"-E{last:00}";
                    }

                    if (episodeNumberMatch.Groups["remaining"].Success)
                    {
                        pathTitle += $" {episodeNumberMatch.Groups["remaining"].Value}";
                    }

                    var parsedFileInfo = ParseTitle(pathTitle);

                    Logger.Debug("Episode parsed from file and folder names. {0}", parsedFileInfo);

                    return parsedFileInfo;
                }
            }

            if (result == null && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out var number))
            {
                Logger.Debug("Attempting to parse episode info using directory and file names. {0}", fileInfo.Directory.Name);
                result = ParseTitle(fileInfo.Directory.Name);

                if (result != null && result.AbsoluteEpisodeNumbers.Contains(number))
                {
                    result.AbsoluteEpisodeNumbers = new[] { number };
                }
                else if (result != null && result.EpisodeNumbers.Contains(number))
                {
                    result.EpisodeNumbers = new[] { number };
                }
                else
                {
                    result = null;
                }
            }

            // TODO: Skip for anime specials

            if (result == null)
            {
                Logger.Debug("Attempting to parse episode info using combined directory and file names. {0}", fileInfo.Directory.Name);
                result = ParseTitle(fileInfo.Directory.Name + " " + fileName);
            }

            if (result == null)
            {
                Logger.Debug("Attempting to parse episode info using directory name. {0}", fileInfo.Directory.Name);
                result = ParseTitle(fileInfo.Directory.Name + fileInfo.Extension);
            }

            return result;
        }

        public static string SimplifyTitle(string title)
        {
            if (!ValidateBeforeParsing(title))
            {
                return title;
            }

            Logger.Debug("Parsing string '{0}'", title);

            if (ReversedTitleRegex.IsMatch(title))
            {
                var titleWithoutExtension = FileExtensions.RemoveFileExtension(title).ToCharArray();
                Array.Reverse(titleWithoutExtension);

                title = string.Concat(new string(titleWithoutExtension), title.AsSpan(titleWithoutExtension.Length));

                Logger.Debug("Reversed name detected. Converted to '{0}'", title);
            }

            var simpleTitle = title;

            simpleTitle = ParserCommon.WebsitePrefixRegex.Replace(simpleTitle);
            simpleTitle = ParserCommon.WebsitePostfixRegex.Replace(simpleTitle);
            simpleTitle = ParserCommon.CleanTorrentSuffixRegex.Replace(simpleTitle);

            return simpleTitle;
        }

        public static int? ParseSeasonFolder(string folderName)
        {
            var seasonMatch = SeasonFolderRegex.Match(folderName);

            if (seasonMatch.Success && seasonMatch.Groups["season"].Success)
            {
                return ParseNumber(seasonMatch.Groups["season"].Value);
            }

            var folderInfo = ParseTitle(folderName);

            if (folderInfo is { FullSeason: true, SeasonNumbers.Count: 1 })
            {
                return folderInfo.SeasonNumbers.First();
            }

            return null;
        }

        public static ParsedEpisodeInfo ParseTitle(string title)
        {
            try
            {
                if (!ValidateBeforeParsing(title))
                {
                    return null;
                }

                Logger.Debug("Parsing string '{0}'", title);

                if (ReversedTitleRegex.IsMatch(title))
                {
                    var titleWithoutExtension = FileExtensions.RemoveFileExtension(title).ToCharArray();
                    Array.Reverse(titleWithoutExtension);

                    title = string.Concat(new string(titleWithoutExtension), title.AsSpan(titleWithoutExtension.Length));

                    Logger.Debug("Reversed name detected. Converted to '{0}'", title);
                }

                var releaseTitle = FileExtensions.RemoveFileExtension(title);

                releaseTitle = releaseTitle.Replace("【", "[").Replace("】", "]");

                foreach (var replace in ParserCommon.PreSubstitutionRegex)
                {
                    if (replace.TryReplace(ref releaseTitle))
                    {
                        Logger.Trace($"Replace regex: {replace}");
                        Logger.Debug("Substituted with " + releaseTitle);
                    }
                }

                var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);

                // TODO: Quick fix stripping [url] - prefixes and postfixes.
                simpleTitle = ParserCommon.WebsitePrefixRegex.Replace(simpleTitle);
                simpleTitle = ParserCommon.WebsitePostfixRegex.Replace(simpleTitle);
                simpleTitle = ParserCommon.CleanTorrentSuffixRegex.Replace(simpleTitle);

                simpleTitle = CleanQualityBracketsRegex.Replace(simpleTitle, m =>
                {
                    if (QualityParser.ParseQualityName(m.Value).Quality != Qualities.Quality.Unknown)
                    {
                        return string.Empty;
                    }

                    return m.Value;
                });

                var sixDigitAirDateMatch = SixDigitAirDateRegex.Match(simpleTitle);
                if (sixDigitAirDateMatch.Success)
                {
                    var airYear = sixDigitAirDateMatch.Groups["airyear"].Value;
                    var airMonth = sixDigitAirDateMatch.Groups["airmonth"].Value;
                    var airDay = sixDigitAirDateMatch.Groups["airday"].Value;

                    if (airMonth != "00" || airDay != "00")
                    {
                        var fixedDate = string.Format("20{0}.{1}.{2}", airYear, airMonth, airDay);

                        simpleTitle = simpleTitle.Replace(sixDigitAirDateMatch.Groups["airdate"].Value, fixedDate);
                    }
                }

                foreach (var regex in ReportTitleRegex)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseMatchCollection(match, releaseTitle);

                            if (result != null)
                            {
                                if (result.FullSeason && result.ReleaseTokens.ContainsIgnoreCase("Special"))
                                {
                                    result.FullSeason = false;
                                    result.Special = true;
                                }

                                result.Languages = LanguageParser.ParseLanguages(result.ReleaseTokens);
                                Logger.Debug("Languages parsed: {0}", string.Join(", ", result.Languages));

                                result.Quality = QualityParser.ParseQuality(title);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                result.ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle);

                                var subGroup = GetSubGroup(match);
                                if (!subGroup.IsNullOrWhiteSpace())
                                {
                                    result.ReleaseGroup = subGroup;
                                }

                                Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                                result.ReleaseHash = GetReleaseHash(match);
                                if (!result.ReleaseHash.IsNullOrWhiteSpace())
                                {
                                    Logger.Debug("Release Hash parsed: {0}", result.ReleaseHash);
                                }

                                return result;
                            }
                        }
                        catch (InvalidDateException ex)
                        {
                            Logger.Debug(ex, ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!title.ToLower().Contains("password") && !title.ToLower().Contains("yenc"))
                {
                    Logger.Error(e, "An error has occurred while trying to parse {0}", title);
                }
            }

            Logger.Debug("Unable to parse {0}", title);
            return null;
        }

        public static ParsedEpisodeInfo ParseSeasonTitle(string title)
        {
            var releaseTitle = FileExtensions.RemoveFileExtension(title);

            if (AnimeSpecialMarkerRegex.IsMatch(releaseTitle))
            {
                return null;
            }

            // Anime season packs are commonly named after the season's own title with no season or
            // episode numbers. ParsingService will determine the correct series and season number
            // from scene mappings.
            var seriesTitle = AnimeSeasonReleaseMetadataRegex.Replace(releaseTitle, " ");
            seriesTitle = DuplicateSpacesRegex.Replace(seriesTitle, " ");
            seriesTitle = seriesTitle.Trim();

            // If any brackets remain we were unable to extract the season title correctly.
            if (seriesTitle.IsNullOrWhiteSpace() || seriesTitle.IndexOfAny(['[', ']', '(', ')', '{', '}']) >= 0)
            {
                return null;
            }

            var result = new ParsedEpisodeInfo
            {
                ReleaseTitle = releaseTitle,
                SeriesTitle = seriesTitle,
                SeriesTitleInfo = new SeriesTitleInfo
                {
                    Title = seriesTitle,
                    TitleWithoutYear = seriesTitle
                },
                EpisodeNumbers = [],
                AbsoluteEpisodeNumbers = [],
                FullSeason = true,
                IsSeasonTitle = true,
                Languages = LanguageParser.ParseLanguages(releaseTitle),
                Quality = QualityParser.ParseQuality(title),
                ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle)
            };

            Logger.Debug("Title only parse. {0}", result);

            return result;
        }

        public static string ParseSeriesName(string title)
        {
            Logger.Debug("Parsing string '{0}'", title);

            var parseResult = ParseTitle(title);

            if (parseResult == null)
            {
                return CleanSeriesTitle(title);
            }

            return parseResult.SeriesTitle;
        }

        public static string CleanSeriesTitle(this string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return title;
            }

            // If Title only contains numbers return it as is.
            if (long.TryParse(title, out _))
            {
                return title;
            }

            // Replace `%` with `percent` to deal with the 3% case
            title = PercentRegex.Replace(title, "percent");

            return NormalizeRegex.Replace(title).ToLowerInvariant().RemoveDiacritics();
        }

        public static string NormalizeEpisodeTitle(string title)
        {
            var match = SpecialEpisodeTitleRegex
                        .Select(v => v.Match(title))
                        .FirstOrDefault(v => v.Success);

            if (match != null)
            {
                title = match.Groups["episodetitle"].Value;
            }

            // Disabled, Until we run into specific testcases for the removal of these words.
            // title = SpecialEpisodeWordRegex.Replace(title, string.Empty);

            title = PartRegex.Replace(title, "");
            title = PunctuationRegex.Replace(title, " ");
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string NormalizeTitle(string title)
        {
            title = PunctuationRegex.Replace(title, string.Empty);
            title = ArticleWordRegex.Replace(title, string.Empty);
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string NormalizeImdbId(string imdbId)
        {
            var imdbRegex = new Regex(@"^(\d{1,10}|(tt)\d{1,10})$");

            if (!imdbRegex.IsMatch(imdbId))
            {
                return null;
            }

            if (imdbId.Length > 2)
            {
                imdbId = imdbId.Replace("tt", "").PadLeft(7, '0');
                return $"tt{imdbId}";
            }

            return null;
        }

        public static string RemoveFileExtension(string title)
        {
            return FileExtensions.RemoveFileExtension(title);
        }

        public static bool HasMultipleLanguages(string title)
        {
            return MultiRegex.IsMatch(title);
        }

        private static SeriesTitleInfo GetSeriesTitleInfo(string title, MatchCollection matchCollection)
        {
            var seriesTitleInfo = new SeriesTitleInfo();
            seriesTitleInfo.Title = title;

            var match = YearInTitleRegex.Match(title);

            if (!match.Success)
            {
                seriesTitleInfo.TitleWithoutYear = title;
            }
            else
            {
                seriesTitleInfo.TitleWithoutYear = match.Groups["title"].Value;
                seriesTitleInfo.Year = Convert.ToInt32(match.Groups["year"].Value);
            }

            var matchComponents = TitleComponentsRegex.Match(seriesTitleInfo.TitleWithoutYear);

            if (matchComponents.Success)
            {
                seriesTitleInfo.AllTitles = matchComponents.Groups["title"].Captures.OfType<Capture>().Select(v => v.Value).ToArray();
            }
            else if (matchCollection[0].Groups["title"].Captures.Count > 1)
            {
                seriesTitleInfo.AllTitles = matchCollection[0].Groups["title"].Captures.Select(c => c.Value.Replace('.', ' ').Replace('_', ' ')).ToArray();
            }

            return seriesTitleInfo;
        }

        private static ParsedEpisodeInfo ParseMatchCollection(MatchCollection matchCollection, string releaseTitle)
        {
            var seriesName = matchCollection[0].Groups["title"].Value.Replace('.', ' ').Replace('_', ' ');
            seriesName = RequestInfoRegex.Replace(seriesName, "").Trim(' ');

            int.TryParse(matchCollection[0].Groups["airyear"].Value, out var airYear);

            var lastSeasonEpisodeStringIndex = matchCollection[0].Groups["title"].EndIndex();

            ParsedEpisodeInfo result;

            if (airYear < 1900)
            {
                result = new ParsedEpisodeInfo
                {
                    ReleaseTitle = releaseTitle,
                    SeasonNumbers = [0],
                    EpisodeNumbers = Array.Empty<int>(),
                    AbsoluteEpisodeNumbers = Array.Empty<int>()
                };

                foreach (Match matchGroup in matchCollection)
                {
                    var episodeCaptures = matchGroup.Groups["episode"].Captures.Cast<Capture>().ToList();
                    var absoluteEpisodeCaptures = matchGroup.Groups["absoluteepisode"].Captures.Cast<Capture>().ToList();

                    // Allows use to return a list of 0 episodes (We can handle that as a full season release)
                    if (episodeCaptures.Any())
                    {
                        var first = ParseNumber(episodeCaptures.First().Value);
                        var last = ParseNumber(episodeCaptures.Last().Value);

                        if (first > last)
                        {
                            return null;
                        }

                        var count = last - first + 1;
                        result.EpisodeNumbers = Enumerable.Range(first, count).ToArray();

                        lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, episodeCaptures.Last().EndIndex());

                        if (matchGroup.Groups["special"].Success)
                        {
                            result.Special = true;
                        }

                        if (matchGroup.Groups["splitepisode"].Success)
                        {
                            result.IsSplitEpisode = true;
                        }
                    }

                    if (absoluteEpisodeCaptures.Any())
                    {
                        var first = ParseDecimal(absoluteEpisodeCaptures.First().Value);
                        var last = ParseDecimal(absoluteEpisodeCaptures.Last().Value);

                        if (first > last)
                        {
                            return null;
                        }

                        if ((first % 1) != 0 || (last % 1) != 0)
                        {
                            if (absoluteEpisodeCaptures.Count != 1)
                            {
                                return null; // Multiple matches not allowed for specials
                            }

                            result.SpecialAbsoluteEpisodeNumbers = new decimal[] { first };
                            result.Special = true;

                            lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, absoluteEpisodeCaptures.First().EndIndex());
                        }
                        else
                        {
                            var count = last - first + 1;
                            result.AbsoluteEpisodeNumbers = Enumerable.Range((int)first, (int)count).ToArray();

                            if (matchGroup.Groups["special"].Success)
                            {
                                result.Special = true;
                            }

                            lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, absoluteEpisodeCaptures.Last().EndIndex());
                        }
                    }

                    if (!episodeCaptures.Any() && !absoluteEpisodeCaptures.Any())
                    {
                        // Check to see if this is an "Extras" or "SUBPACK" release, if it is, set
                        // IsSeasonExtra so they can be filtered out
                        if (!matchCollection[0].Groups["extras"].Value.IsNullOrWhiteSpace())
                        {
                            result.IsSeasonExtra = true;
                        }

                        // Partial season packs will have a seasonpart group so they can be differentiated
                        // from a full season/single episode release
                        var seasonPart = matchCollection[0].Groups["seasonpart"].Value;

                        if (seasonPart.IsNotNullOrWhiteSpace())
                        {
                            result.SeasonPart = Convert.ToInt32(seasonPart);
                            result.IsPartialSeason = true;
                        }
                        else if (matchCollection[0].Groups["special"].Success)
                        {
                            result.Special = true;
                        }
                        else
                        {
                            result.FullSeason = true;
                        }
                    }

                    if (episodeCaptures.Count == 2 && matchCollection[0].Groups["episodecount"].Success && episodeCaptures.Last().Value == matchCollection[0].Groups["episodecount"].Value)
                    {
                        result.EpisodeNumbers = Array.Empty<int>();
                        result.FullSeason = true;
                    }
                }

                var seasons = new List<int>();

                foreach (Capture seasonCapture in matchCollection[0].Groups["season"].Captures)
                {
                    if (int.TryParse(seasonCapture.Value, out var parsedSeason))
                    {
                        seasons.Add(parsedSeason);

                        lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, seasonCapture.EndIndex());
                    }
                }

                var distinctSeasons = seasons.Distinct().OrderBy(s => s).ToList();

                if (distinctSeasons.Count is 1 or > 2)
                {
                    result.SeasonNumbers = distinctSeasons;
                }
                else if (distinctSeasons.Count == 2)
                {
                    result.SeasonNumbers = Enumerable.Range(distinctSeasons.First(), distinctSeasons.Last() - distinctSeasons.First() + 1).ToList();
                }
                else if (!result.AbsoluteEpisodeNumbers.Any() && result.EpisodeNumbers.Any())
                {
                    // If no season was found and it's not an absolute only release it should be treated as a mini series and season 1
                    result.SeasonNumbers = [1];
                    result.IsMiniSeries = true;
                }
            }
            else
            {
                // Try to Parse as a daily show

                var airmonth = 0;
                var airday = 0;

                if (matchCollection[0].Groups["ambiguousairmonth"].Success &&
                    matchCollection[0].Groups["ambiguousairday"].Success)
                {
                    var ambiguousAirMonth = Convert.ToInt32(matchCollection[0].Groups["ambiguousairmonth"].Value);
                    var ambiguousAirDay = Convert.ToInt32(matchCollection[0].Groups["ambiguousairday"].Value);

                    if (ambiguousAirDay <= 12 && ambiguousAirMonth <= 12)
                    {
                        throw new InvalidDateException("Ambiguous Date, cannot validate month and day with {0} and {1}", ambiguousAirMonth, ambiguousAirDay);
                    }

                    airmonth = ambiguousAirMonth;
                    airday = ambiguousAirDay;
                }
                else if (matchCollection[0].Groups["shortairmonth"].Success)
                {
                    var shortMonthValue = matchCollection[0].Groups["shortairmonth"].Value;

                    if (ShortMonths.TryGetValue(shortMonthValue.ToLowerInvariant(), out var shortMonth))
                    {
                        airmonth = shortMonth;
                        airday = Convert.ToInt32(matchCollection[0].Groups["airday"].Value);
                    }
                    else
                    {
                        throw new InvalidDateException("Unable to determine air month from month: {0}", shortMonthValue);
                    }
                }
                else
                {
                    airmonth = Convert.ToInt32(matchCollection[0].Groups["airmonth"].Value);
                    airday = Convert.ToInt32(matchCollection[0].Groups["airday"].Value);
                }

                // Swap day and month if month is bigger than 12 (scene fail)
                if (airmonth > 12)
                {
                    (airday, airmonth) = (airmonth, airday);
                }

                DateTime airDate;

                try
                {
                    airDate = new DateTime(airYear, airmonth, airday);
                }
                catch (Exception)
                {
                    throw new InvalidDateException("Invalid date found: {0}-{1}-{2}", airYear, airmonth, airday);
                }

                // Allow early releases, but reject dates too far in the future (most likely a parse error)
                if (airDate > DateTime.Now.AddDays(14).Date)
                {
                    throw new InvalidDateException("Invalid date found: {0}", airDate);
                }

                // If the parsed air date is before 1970 and the title year wasn't matched (not a match for the Plex DVR format) throw an error
                if (airDate < new DateTime(1970, 1, 1) && matchCollection[0].Groups["titleyear"].Value.IsNullOrWhiteSpace())
                {
                    throw new InvalidDateException("Invalid date found: {0}", airDate);
                }

                lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, matchCollection[0].Groups["airyear"].EndIndex());
                lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, matchCollection[0].Groups["airmonth"].EndIndex());
                lastSeasonEpisodeStringIndex = Math.Max(lastSeasonEpisodeStringIndex, matchCollection[0].Groups["airday"].EndIndex());

                result = new ParsedEpisodeInfo
                {
                    ReleaseTitle = releaseTitle,
                    SeasonNumbers = [0],
                    AirDate = airDate.ToString(AirDateFormat),
                };

                var partMatch = matchCollection[0].Groups["part"];

                if (partMatch.Success)
                {
                    result.DailyPart = Convert.ToInt32(partMatch.Value);
                }
            }

            if (lastSeasonEpisodeStringIndex < releaseTitle.Length)
            {
                result.ReleaseTokens = releaseTitle.Substring(lastSeasonEpisodeStringIndex);
            }
            else
            {
                result.ReleaseTokens = releaseTitle;
            }

            result.SeriesTitle = seriesName;
            result.SeriesTitleInfo = GetSeriesTitleInfo(result.SeriesTitle, matchCollection);

            Logger.Debug("Episode Parsed. {0}", result);

            return result;
        }

        private static bool ValidateBeforeParsing(string title)
        {
            if (title.ToLower().Contains("password") && title.ToLower().Contains("yenc"))
            {
                Logger.Debug("");
                return false;
            }

            if (!title.Any(char.IsLetterOrDigit))
            {
                return false;
            }

            var titleWithoutExtension = FileExtensions.RemoveFileExtension(title);

            if (RejectHashedReleasesRegexes.Any(v => v.IsMatch(titleWithoutExtension)))
            {
                Logger.Debug("Rejected Hashed Release Title: " + title);
                return false;
            }

            if (SeasonFolderRegexes.Any(v => v.IsMatch(titleWithoutExtension)))
            {
                Logger.Debug("Rejected Season Folder Release Title: " + title);
                return false;
            }

            return true;
        }

        private static string GetSubGroup(MatchCollection matchCollection)
        {
            var subGroup = matchCollection[0].Groups["subgroup"];

            if (subGroup.Success)
            {
                return subGroup.Value;
            }

            return string.Empty;
        }

        private static string GetReleaseHash(MatchCollection matchCollection)
        {
            var hash = matchCollection[0].Groups["hash"];

            if (hash.Success)
            {
                var hashValue = hash.Value.Trim('[', ']', '(', ')');

                if (hashValue.Equals("1280x720"))
                {
                    return string.Empty;
                }

                return hashValue;
            }

            return string.Empty;
        }

        private static int ParseNumber(string value)
        {
            var normalized = ConvertToNumerals(value.Normalize(NormalizationForm.FormKC));

            if (int.TryParse(normalized, out var number))
            {
                return number;
            }

            number = Array.IndexOf(Numbers, value.ToLower());

            if (number != -1)
            {
                return number;
            }

            throw new FormatException(string.Format("{0} isn't a number", value));
        }

        private static decimal ParseDecimal(string value)
        {
            var normalized = ConvertToNumerals(value.Normalize(NormalizationForm.FormKC));

            if (decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            throw new FormatException(string.Format("{0} isn't a number", value));
        }

        private static string ConvertToNumerals(string input)
        {
            var result = new StringBuilder(input.Length);

            foreach (var c in input.ToCharArray())
            {
                if (char.IsNumber(c))
                {
                    result.Append(char.GetNumericValue(c));
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
