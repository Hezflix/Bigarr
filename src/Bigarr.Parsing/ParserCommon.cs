// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/ParserCommon.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System.Text.RegularExpressions;

namespace Bigarr.Parsing;

// These are functions shared between different parser functions
// they are not intended to be used outside of them parsing.
internal static partial class ParserCommon
{
    internal static readonly RegexReplace[] PreSubstitutionRegex = System.Array.Empty<RegexReplace>();

    // Valid TLDs http://data.iana.org/TLD/tlds-alpha-by-domain.txt
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
