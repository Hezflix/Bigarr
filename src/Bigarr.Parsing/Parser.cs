// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/Parser.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bigarr.Parsing.Internal;
using Bigarr.Parsing.Model;

namespace Bigarr.Parsing
{
    public static partial class Parser
    {
        private static readonly ParserLog Logger = ParserLog.For(typeof(Parser));

        private const string EditionPattern = @"\(?\b(?<edition>(((Recut.|Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Extended|Despecialized|(Special|Rouge|Final|Assembly|Imperial|Diamond|Signature|Hunter|Rekall)(?=(.(Cut|Edition|Version)))|\d{2,3}(th)?.Anniversary)(?:.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|Open.?Matte|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|Open?.Matte|IMAX|Fan.?Edit|Restored|((2|3|4)in1))))))\b\)?";

        [GeneratedRegex(EditionPattern, RegexOptions.IgnoreCase)]
        private static partial Regex EditionRegex { get; }

        [GeneratedRegex(@"^.+?" + EditionPattern, RegexOptions.IgnoreCase)]
        private static partial Regex ReportEditionRegex { get; }

        [GeneratedRegex(@"\b((?<hcsub>(\w+(?<!SOFT|MULTI|HORRIBLE)SUBS?))|(?<hc>(HC|SUBBED)))\b", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
        private static partial Regex HardcodedSubsRegex { get; }

        private static readonly Regex[] ReportMovieTitleRegex = new[]
        {
            // Anime [Subgroup] and Year
            ReportMovieTitleRegex1,

            // Anime [Subgroup] no year, versioned title, hash
            ReportMovieTitleRegex2,

            // Anime [Subgroup] no year, info in double sets of brackets, hash
            ReportMovieTitleRegex3,

            // Anime [Subgroup] no year, info in parentheses or brackets, hash
            ReportMovieTitleRegex4,

            // Some german or french tracker formats (missing year, ...) (Only applies to german and TrueFrench releases) - see ParserFixture for examples and tests - french removed as it broke all movies w/ french titles
            ReportMovieTitleEditionRegex1,

            // Special, Despecialized, etc. Edition Movies, e.g: Mission.Impossible.3.Special.Edition.2011
            ReportMovieTitleEditionRegex2,

            // Special, Despecialized, etc. Edition Movies, e.g: Mission.Impossible.3.2011.Special.Edition //TODO: Seems to slow down parsing heavily!
            /*new Regex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(19|20)\d{2}(?!p|i|(19|20)\d{2}|\]|\W(19|20)\d{2})))+(\W+|_|$)(?!\\)\(?(?<edition>(((Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Final(?=(.(Cut|Edition|Version)))|Extended|Rogue|Special|Despecialized|\d{2,3}(th)?.Anniversary)(.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|IMAX|Fan.?Edit|Edition|Restored|((2|3|4)in1))))))\)?",
                          RegexOptions.IgnoreCase | RegexOptions.Compiled),*/

            // Normal movie format, e.g: Mission.Impossible.3.2011
            ReportMovieTitleRegex5,

            // PassThePopcorn Torrent names: Star.Wars[PassThePopcorn]
            ReportMovieTitleRegex6,

            // That did not work? Maybe some tool uses [] for years. Who would do that?
            ReportMovieTitleRegex7,

            // As a last resort for movies that have ( or [ in their title.
            ReportMovieTitleRegex8
        };

        [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*" + EditionPattern + @".{1,3}(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleEditionRegex2 { get; }

        [GeneratedRegex(@"^(?<title>(?![(\[]).+?)((\W|_))(" + EditionPattern + @".{1,3})?(?:(?<!(19|20)\d{2}.*?)(?<!(?:Good|The)[_ .-])(German|TrueFrench))(.+?)(?=((19|20)\d{2}|$))(?<year>(19|20)\d{2}(?!p|i|\d+|\]|\W\d+))?(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleEditionRegex1 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|x|\d+|\]|\W\d+)))+.*?(?<hash>\[\w{8}\])?(?:$|\.)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex1 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)((v)(?:\d{1,2})(?:([-_. ])))(\[.*)?(?:[\[(][^])])?.*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex2 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)(\[.*).*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex3 { get; }

        [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+)(?:[\[(][^])]).*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex4 { get; }

        [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex5 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![()\[!]))*(?<year>(\[\w *\])))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex6 { get; }

        [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex7 { get; }

        [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportMovieTitleRegex8 { get; }

        private static readonly Regex[] ReportMovieTitleFolderRegex = new[]
        {
            // When year comes first.
            new Regex(@"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$")
        };

        private static readonly Regex[] RejectHashedReleasesRegex = new Regex[]
            {
                // Generic match for md5 and mixed-case hashes.
                RejectHashedReleasesRegex1,

                // Generic match for shorter lower-case hashes.
                RejectHashedReleasesRegex2,

                // Format seen on some NZBGeek releases
                // Be very strict with these coz they are very close to the valid 101 ep numbering.
                RejectHashedReleasesRegex3,
                RejectHashedReleasesRegex4,

                // Backup filename (Unknown origins)
                RejectHashedReleasesRegex5,

                // 123 - Started appearing December 2014
                RejectHashedReleasesRegex6,

                // abc - Started appearing January 2015
                RejectHashedReleasesRegex7,

                // abc - Started appearing 2020
                RejectHashedReleasesRegex8,

                // b00bs - Started appearing January 2015
                RejectHashedReleasesRegex9
            };

        [GeneratedRegex(@"^[0-9a-zA-Z]{32}")]
        private static partial Regex RejectHashedReleasesRegex1 { get; }

        [GeneratedRegex(@"^[a-z0-9]{24}$")]
        private static partial Regex RejectHashedReleasesRegex2 { get; }

        [GeneratedRegex(@"^[A-Z]{11}\d{3}$")]
        private static partial Regex RejectHashedReleasesRegex3 { get; }

        [GeneratedRegex(@"^[a-z]{12}\d{3}$")]
        private static partial Regex RejectHashedReleasesRegex4 { get; }

        [GeneratedRegex(@"^Backup_\d{5,}S\d{2}-\d{2}$")]
        private static partial Regex RejectHashedReleasesRegex5 { get; }

        [GeneratedRegex(@"^123$")]
        private static partial Regex RejectHashedReleasesRegex6 { get; }

        [GeneratedRegex(@"^abc$", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegex7 { get; }

        [GeneratedRegex(@"^abc[-_. ]xyz", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegex8 { get; }

        [GeneratedRegex(@"^b00bs$", RegexOptions.IgnoreCase)]
        private static partial Regex RejectHashedReleasesRegex9 { get; }

        // Regex to detect whether the title was reversed.
        [GeneratedRegex(@"(?:^|[-._ ])(p027|p0801)[-._ ]")]
        private static partial Regex ReversedTitleRegex { get; }

        // Regex to split movie titles that contain `AKA`.
        [GeneratedRegex(@"[ ]+(?:AKA|\/)[ ]+", RegexOptions.IgnoreCase)]
        private static partial Regex AlternativeTitleRegex { get; }

        // Regex to unbracket alternative titles.
        [GeneratedRegex(@"(.*) \([ ]*AKA[ ]+(.*)\)", RegexOptions.IgnoreCase)]
        private static partial Regex BracketedAlternativeTitleRegex { get; }

        [GeneratedRegex(@"[ ]+(?:A\.K\.A\.)[ ]+", RegexOptions.IgnoreCase)]
        private static partial Regex NormalizeAlternativeTitleRegex { get; }

        [GeneratedRegex(@"((?:\b|_)(?<!^|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])([aà](?!$|[^a-zA-Z0-9_']\w[^a-zA-Z0-9_'])|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", RegexOptions.IgnoreCase)]
        private static partial Regex NormalizeRegex { get; }

        [GeneratedRegex(@"(?<imdbid>tt\d{7,8})", RegexOptions.IgnoreCase)]
        private static partial Regex ReportImdbId { get; }
        [GeneratedRegex(@"tmdb(id)?-(?<tmdbid>\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex ReportTmdbId { get; }

        private static readonly RegexReplace SimpleTitleRegex = new RegexReplace(SimpleTitleRegex1, string.Empty);

        [GeneratedRegex(@"(?:(480|540|576|720|1080|2160)[ip]|[xh][\W_]?26[45]|DD\W?5\W1|[<>?*]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(8|10)b(it)?|10-bit)\s*?(?![a-b0-9])", RegexOptions.IgnoreCase)]
        private static partial Regex SimpleTitleRegex1 { get; }

        [GeneratedRegex(@"\s*(?:[<>?*|])", RegexOptions.IgnoreCase)]
        private static partial Regex SimpleReleaseTitleRegex { get; }

        // Valid TLDs http://data.iana.org/TLD/tlds-alpha-by-domain.txt

        [GeneratedRegex(@"\[[a-z0-9 ._-]+\]$", RegexOptions.IgnoreCase)]
        private static partial Regex CleanQualityBracketsRegex { get; }

        [GeneratedRegex(@"^(?<title>.+?)(?:\W|_.)?[\(\[]?(?<year>\d{4})[\]\)]?", RegexOptions.IgnoreCase)]
        private static partial Regex YearInTitleRegex { get; }

        [GeneratedRegex(@"(\&|\:|\\|\/)+")]
        private static partial Regex SpecialCharRegex { get; }
        [GeneratedRegex(@"[^\w\s]")]
        private static partial Regex PunctuationRegex { get; }
        [GeneratedRegex(@"^(a|an|the)\s", RegexOptions.IgnoreCase)]
        private static partial Regex ArticleWordRegex { get; }
        [GeneratedRegex(@"\b(part|special|edition|christmas)\b\s?", RegexOptions.IgnoreCase)]
        private static partial Regex SpecialEpisodeWordRegex { get; }
        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex DuplicateSpacesRegex { get; }

        [GeneratedRegex(@"^(?:\[.+?\])+")]
        private static partial Regex RequestInfoRegex { get; }

        private static readonly string[] Numbers = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        [GeneratedRegex(@"[_. ](?<multi>multi)[_. ]", RegexOptions.IgnoreCase)]
        private static partial Regex MultiRegex { get; }

        private static Dictionary<string, string> _umlautMappings = new Dictionary<string, string>
        {
            { "ö", "oe" },
            { "ä", "ae" },
            { "ü", "ue" },
        };

        public static ParsedMovieInfo ParseMoviePath(string path)
        {
            var fileInfo = new FileInfo(path);

            var result = ParseMovieTitle(fileInfo.Name, true);

            if (result == null)
            {
                Logger.Debug("Attempting to parse movie info using directory and file names. {0}", fileInfo.Directory.Name);
                result = ParseMovieTitle(fileInfo.Directory.Name + " " + fileInfo.Name);
            }

            if (result == null)
            {
                Logger.Debug("Attempting to parse movie info using directory name. {0}", fileInfo.Directory.Name);
                result = ParseMovieTitle(fileInfo.Directory.Name + fileInfo.Extension);
            }

            return result;
        }

        public static ParsedMovieInfo ParseMovieTitle(string title, bool isDir = false)
        {
            var originalTitle = title;
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

                    title = $"{titleWithoutExtension}{title.Substring(titleWithoutExtension.Length)}";

                    Logger.Debug("Reversed name detected. Converted to '{0}'", title);
                }

                var releaseTitle = FileExtensions.RemoveFileExtension(title);

                // Trim dashes from end
                releaseTitle = releaseTitle.Trim('-', '_');

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

                // TODO: Quick fix stripping [url] - prefixes.
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

                var allRegexes = ReportMovieTitleRegex.ToList();

                if (isDir)
                {
                    allRegexes.AddRange(ReportMovieTitleFolderRegex);
                }

                foreach (var regex in allRegexes)
                {
                    var match = regex.Matches(simpleTitle);

                    if (match.Count != 0)
                    {
                        Logger.Trace(regex);
                        try
                        {
                            var result = ParseMovieMatchCollection(match);

                            if (result != null)
                            {
                                // TODO: Add tests for this!
                                var simpleReleaseTitle = SimpleReleaseTitleRegex.Replace(releaseTitle, string.Empty);

                                var simpleTitleReplaceString = match[0].Groups["title"].Success ? match[0].Groups["title"].Value : result.PrimaryMovieTitle;

                                if (simpleTitleReplaceString.IsNotNullOrWhiteSpace())
                                {
                                    if (match[0].Groups["title"].Success)
                                    {
                                        simpleReleaseTitle = simpleReleaseTitle.Remove(match[0].Groups["title"].Index, match[0].Groups["title"].Length)
                                                                               .Insert(match[0].Groups["title"].Index, simpleTitleReplaceString.Contains('.') ? "A.Movie" : "A Movie");
                                    }
                                    else
                                    {
                                        simpleReleaseTitle = simpleReleaseTitle.Replace(simpleTitleReplaceString, simpleTitleReplaceString.Contains('.') ? "A.Movie" : "A Movie");
                                    }
                                }

                                result.ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(simpleReleaseTitle);

                                var subGroup = GetSubGroup(match);
                                if (!subGroup.IsNullOrWhiteSpace())
                                {
                                    result.ReleaseGroup = subGroup;
                                }

                                result.HardcodedSubs = ParseHardcodeSubs(title);

                                Logger.Debug("Release Group parsed: {0}", result.ReleaseGroup);

                                result.Languages = LanguageParser.ParseLanguages(result.ReleaseGroup.IsNotNullOrWhiteSpace() ? simpleReleaseTitle.Replace(result.ReleaseGroup, "RlsGrp") : simpleReleaseTitle);
                                Logger.Debug("Languages parsed: {0}", string.Join(", ", result.Languages));

                                result.Quality = QualityParser.ParseQuality(title);
                                Logger.Debug("Quality parsed: {0}", result.Quality);

                                if (result.Edition.IsNullOrWhiteSpace())
                                {
                                    result.Edition = ParseEdition(simpleReleaseTitle);
                                    Logger.Debug("Edition parsed: {0}", result.Edition);
                                }

                                result.ReleaseHash = GetReleaseHash(match);
                                if (!result.ReleaseHash.IsNullOrWhiteSpace())
                                {
                                    Logger.Debug("Release Hash parsed: {0}", result.ReleaseHash);
                                }

                                result.OriginalTitle = originalTitle;
                                result.ReleaseTitle = releaseTitle;
                                result.SimpleReleaseTitle = simpleReleaseTitle;

                                result.ImdbId = ParseImdbId(simpleReleaseTitle);
                                result.TmdbId = ParseTmdbId(simpleReleaseTitle);

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

        public static string ParseImdbId(string title)
        {
            var match = ReportImdbId.Match(title);
            if (match.Success)
            {
                if (match.Groups["imdbid"].Value != null)
                {
                    if (match.Groups["imdbid"].Length == 9 || match.Groups["imdbid"].Length == 10)
                    {
                        return match.Groups["imdbid"].Value;
                    }
                }
            }

            return "";
        }

        public static int ParseTmdbId(string title)
        {
            var match = ReportTmdbId.Match(title);
            if (match.Success)
            {
                if (match.Groups["tmdbid"].Value != null)
                {
                    return int.TryParse(match.Groups["tmdbid"].Value, out var tmdbId) ? tmdbId : 0;
                }
            }

            return 0;
        }

        public static string ParseEdition(string languageTitle)
        {
            var editionMatch = ReportEditionRegex.Match(languageTitle);

            if (editionMatch.Success && editionMatch.Groups["edition"].Value != null &&
                editionMatch.Groups["edition"].Value.IsNotNullOrWhiteSpace())
            {
                return editionMatch.Groups["edition"].Value.Replace(".", " ");
            }

            return "";
        }

        public static string ReplaceGermanUmlauts(string s)
        {
            var t = s;
            t = t.Replace("ä", "ae");
            t = t.Replace("ö", "oe");
            t = t.Replace("ü", "ue");
            t = t.Replace("Ä", "Ae");
            t = t.Replace("Ö", "Oe");
            t = t.Replace("Ü", "Ue");
            t = t.Replace("ß", "ss");
            return t;
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

        public static string ToUrlSlug(string value, bool invalidDashReplacement = false, string trimEndChars = "-_", string deduplicateChars = "-_")
        {
            // First to lower case
            value = value.ToLowerInvariant();

            // Remove all accents
            value = value.RemoveAccent();

            // Replace spaces
            value = ToUrlSlugRegex1.Replace(value, "-");

            // Should invalid characters be replaced with dash or empty string?
            var replaceCharacter = invalidDashReplacement ? "-" : string.Empty;

            // Remove invalid chars
            value = ToUrlSlugRegex2.Replace(value, replaceCharacter);

            // Trim dashes or underscores from end, or user defined character set
            if (!string.IsNullOrEmpty(trimEndChars))
            {
                value = value.Trim(trimEndChars.ToCharArray());
            }

            // Replace double occurrences of - or _, or user defined character set
            if (!string.IsNullOrEmpty(deduplicateChars))
            {
                value = Regex.Replace(value, @"([" + deduplicateChars + "]){2,}", "$1");
            }

            return value;
        }

        [GeneratedRegex(@"\s")]
        private static partial Regex ToUrlSlugRegex1 { get; }

        [GeneratedRegex(@"[^a-z0-9\s-_]")]
        private static partial Regex ToUrlSlugRegex2 { get; }

        public static string CleanMovieTitle(this string title)
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

            return ReplaceGermanUmlauts(NormalizeRegex.Replace(title, string.Empty).ToLowerInvariant()).RemoveAccent();
        }

        public static string NormalizeMovieTitle(string title)
        {
            title = SpecialEpisodeWordRegex.Replace(title, string.Empty);
            title = PunctuationRegex.Replace(title, " ");
            title = DuplicateSpacesRegex.Replace(title, " ");

            return title.Trim().ToLower();
        }

        public static string NormalizeTitle(string title)
        {
            title = PunctuationRegex.Replace(title, string.Empty);
            title = ArticleWordRegex.Replace(title, string.Empty);
            title = DuplicateSpacesRegex.Replace(title, " ");
            title = SpecialCharRegex.Replace(title, string.Empty);

            return title.Trim().ToLower();
        }

        public static string SimplifyReleaseTitle(this string title)
        {
            return SimpleReleaseTitleRegex.Replace(title, string.Empty);
        }

        public static string ParseHardcodeSubs(string title)
        {
            var subMatch = HardcodedSubsRegex.Matches(title).OfType<Match>().LastOrDefault();

            if (subMatch != null && subMatch.Success)
            {
                if (subMatch.Groups["hcsub"].Success)
                {
                    return subMatch.Groups["hcsub"].Value;
                }
                else if (subMatch.Groups["hc"].Success)
                {
                    return "Generic Hardcoded Subs";
                }
            }

            return null;
        }

        public static bool HasMultipleLanguages(string title)
        {
            return MultiRegex.IsMatch(title);
        }

        private static ParsedMovieInfo ParseMovieMatchCollection(MatchCollection matchCollection)
        {
            if (!matchCollection[0].Groups["title"].Success || matchCollection[0].Groups["title"].Value == "(")
            {
                return null;
            }

            var movieName = matchCollection[0].Groups["title"].Value.Replace('_', ' ');
            movieName = NormalizeAlternativeTitleRegex.Replace(movieName, " AKA ");
            movieName = RequestInfoRegex.Replace(movieName, "").Trim(' ');

            var parts = movieName.Split('.');
            movieName = "";
            var n = 0;
            var previousAcronym = false;
            var nextPart = "";
            foreach (var part in parts)
            {
                if (parts.Length >= n + 2)
                {
                    nextPart = parts[n + 1];
                }
                else
                {
                    nextPart = "";
                }

                if (part.Length == 1 && part.ToLower() != "a" && !int.TryParse(part, out _) &&
                    (previousAcronym || n < parts.Length - 1) &&
                    (previousAcronym || nextPart.Length != 1 || !int.TryParse(nextPart, out _)))
                {
                    movieName += part + ".";
                    previousAcronym = true;
                }
                else if (part.ToLower() == "a" && (previousAcronym || nextPart.Length == 1))
                {
                    movieName += part + ".";
                    previousAcronym = true;
                }
                else if (part.ToLower() == "dr")
                {
                    movieName += part + ".";
                    previousAcronym = true;
                }
                else
                {
                    if (previousAcronym)
                    {
                        movieName += " ";
                        previousAcronym = false;
                    }

                    movieName += part + " ";
                }

                n++;
            }

            movieName = movieName.Trim(' ');

            int.TryParse(matchCollection[0].Groups["year"].Value, out var airYear);

            ParsedMovieInfo result;

            result = new ParsedMovieInfo { Year = airYear };

            if (matchCollection[0].Groups["edition"].Success)
            {
                result.Edition = matchCollection[0].Groups["edition"].Value.Replace(".", " ");
            }

            var movieTitles = new List<string>();
            movieTitles.Add(movieName);

            // Delete parentheses of the form (aka ...).
            var unbracketedName = BracketedAlternativeTitleRegex.Replace(movieName, "$1 AKA $2");

            // Split by AKA and filter out empty and duplicate names.
            movieTitles
                .AddRange(AlternativeTitleRegex
                        .Split(unbracketedName)
                        .Where(alternativeName => alternativeName.IsNotNullOrWhiteSpace() && alternativeName != movieName));

            result.MovieTitles = movieTitles;

            Logger.Debug("Movie Parsed. {0}", result);

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

            if (RejectHashedReleasesRegex.Any(v => v.IsMatch(titleWithoutExtension)))
            {
                Logger.Debug("Rejected Hashed Release Title: " + title);
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
                var hashValue = hash.Value.Trim('[', ']');

                if (hashValue.Equals("1280x720"))
                {
                    return string.Empty;
                }

                return hashValue;
            }

            return string.Empty;
        }
    }
}
