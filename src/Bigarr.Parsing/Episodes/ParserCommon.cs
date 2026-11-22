// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/ParserCommon.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

using System.Text.RegularExpressions;

namespace Bigarr.Parsing.Episodes;

// These are functions shared between different parser functions
// they are not intended to be used outside of them parsing.
internal static partial class ParserCommon
{
    internal static readonly RegexReplace[] PreSubstitutionRegex = new[]
    {
        // Korean series without season number, replace with S01Exxx and remove airdate
        new RegexReplace(PreSubstitutionRegex1, ".S01E$1.$2"),

        // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
        new RegexReplace(PreSubstitutionRegex2, "[${subgroup}] ${title} - ${episode} - "),

        // Chinese LoliHouse/ZERO/Lilith-Raws/Skymoon-Raws/orion origin releases don't use the expected brackets, normalize using brackets
        new RegexReplace(PreSubstitutionRegex3, "[${subgroup}][${title}][${episode}]["),

        // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk first and if it has S0x as season number, convert it to Sx
        new RegexReplace(PreSubstitutionRegex4, "[${subgroup}] ${title} S${season} - ${episode} "),

        // Some Chinese releases don't include a separation between Chinese and English titles within the same bracketed group
        new RegexReplace(PreSubstitutionRegex5, "[${subgroup}] ${title} - ${episode} "),

        // Chinese season packs, that may not actually have the full season so treated as multi-episode
        new RegexReplace(PreSubstitutionRegex6, "${title}S${season}E${episode}.${language}${rest}"),

        // Chinese season packs, that actually have the full season so treated as multi-episode
        new RegexReplace(PreSubstitutionRegex7, "${title}S${season}.${language}${rest}"),

        // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk and replace with normal anime pattern
        new RegexReplace(PreSubstitutionRegex8, "[${subgroup}] ${title} - ${episode} "),

        // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title first and if it has S0x as season number, convert it to Sx
        new RegexReplace(PreSubstitutionRegex9, "[${subgroup}] ${title} S${season} - ${episode} "),

        // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
        new RegexReplace(PreSubstitutionRegex10, "[${subgroup}] ${title} - ${episode} "),

        // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title and replace with normal anime pattern
        new RegexReplace(PreSubstitutionRegex11, "[${subgroup}] ${title} - ${episode} "),

        // GM-Team releases with lots of square brackets
        new RegexReplace(PreSubstitutionRegex12, "[${subgroup}] ${title} - ${episode} "),

        // Some Chinese anime releases contain both Chinese and English titles separated by | instead of /, remove the Chinese title and replace with normal anime pattern
        new RegexReplace(PreSubstitutionRegex13, "[${subgroup}] ${title} - ${episode} "),

        // Spanish releases with information in brackets
        new RegexReplace(PreSubstitutionRegex14, "${title} (${year}) - ${info} "),
    };

    [GeneratedRegex(@"\.E(\d{2,4})\.\d{6}\.(.*-(F1RST|NEXT))$")]
    private static partial Regex PreSubstitutionRegex1 { get; }

    [GeneratedRegex(@"^\[(?:(?<subgroup>[^\]]+?)(?:[\u4E00-\u9FCC]+)?)\]\[(?<title>[^\]]+?)(?:\s(?<chinesetitle>[\u4E00-\u9FCC][^\]]*?))\]\[(?:(?:[\u4E00-\u9FCC]+?)?(?<episode>\d{1,4})(?:[\u4E00-\u9FCC]+?)?)\]")]
    private static partial Regex PreSubstitutionRegex2 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]*?(?:LoliHouse|ZERO|Lilith-Raws|Skymoon-Raws|orion origin)[^\]]*?)\](?<title>[^\[\]]+?)(?: - (?<episode>[0-9-]+)\s*|\[第?(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?\])\[")]
    private static partial Regex PreSubstitutionRegex3 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitutionRegex4 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\]\[(?<chinesetitle>(?<![^a-zA-Z0-9])[^a-zA-Z0-9]+)(?<title>[^\]]+?)\](?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitutionRegex5 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:第|全)?(?<episode>[0-9]{1,4}-[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex PreSubstitutionRegex6 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:全)?(?<episode>[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex PreSubstitutionRegex7 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]{1,4}(?:-[0-9]{1,4})?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitutionRegex8 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s/\s))(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?")]
    private static partial Regex PreSubstitutionRegex9 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<title>[^\]]+?)(?:\s/\s))(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitutionRegex10 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s/\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitutionRegex11 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:(?<chinesubgroup>\[(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*\])+)\[(?<title>[^\]]+?)\](?<junk>\[[^\]]+\])*\[(?<episode>[0-9]+(?:-[0-9]+)?)( END| Fin)?\]")]
    private static partial Regex PreSubstitutionRegex12 { get; }

    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s\|\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitutionRegex13 { get; }

    [GeneratedRegex(@"^(?<title>.+?(?=[ ._-]\()).+?\((?<year>\d{4})\/(?<info>S[^\/]+)")]
    private static partial Regex PreSubstitutionRegex14 { get; }

    internal static readonly RegexReplace[] PathPreSubstitutionRegex = new[]
    {
        // Season and episode number separated by a space (S02 01)
        new RegexReplace(PathPreSubstitutionRegex1, "${separator}S${season}E${episode}"),
    };

    [GeneratedRegex(@"(?<separator>^|[-_. ])S(?<season>(?<!\d+)\d{1,2}(?!\d+)) (?<episode>(?<!\d+)\d{1,2}(?!\d+))(?!\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PathPreSubstitutionRegex1 { get; }

    internal static readonly RegexReplace WebsitePrefixRegex = new RegexReplace(WebsitePrefixRegex1, string.Empty);

    [GeneratedRegex(@"^(?:(?:\[|\()\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?<!Naruto-Kun\.)(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*(?:\]|\))|[ -]{2,})[ -]*", RegexOptions.IgnoreCase)]
    private static partial Regex WebsitePrefixRegex1 { get; }

    internal static readonly RegexReplace WebsitePostfixRegex = new RegexReplace(WebsitePostfixRegex1, string.Empty);

    [GeneratedRegex(@"(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:xn--[a-z0-9-]{4,}|[a-z]{2,6})\b(?:\s*\])$", RegexOptions.IgnoreCase)]
    private static partial Regex WebsitePostfixRegex1 { get; }

    internal static readonly RegexReplace CleanTorrentSuffixRegex = new RegexReplace(CleanTorrentSuffixRegex1, string.Empty);

    [GeneratedRegex(@"\[(?:ettv|rartv|rarbg|cttv|publichd)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanTorrentSuffixRegex1 { get; }
}
