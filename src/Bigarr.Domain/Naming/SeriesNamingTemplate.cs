using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Naming;

/// <summary>Which templates a series token may appear in.</summary>
public enum SeriesTokenScope
{
    /// <summary>Anywhere: series folder, season folder, episode file.</summary>
    Series = 1,

    /// <summary>Season folder and episode file.</summary>
    Season = 2,

    /// <summary>Episode file only: depends on the file.</summary>
    Episode = 3,
}

/// <summary>The series templates of media management.</summary>
public enum SeriesTemplateKind
{
    SeriesFolder = 1,
    SeasonFolder = 2,
    SpecialsFolder = 3,
    Standard = 4,
    Daily = 5,
    Anime = 6,
}

public sealed record SeriesNamingToken(string Name, string Description, string Example, SeriesTokenScope Scope);

/// <summary>
/// Sonarr-compatible naming: <c>{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}</c>. Same
/// grammar as the film templates (<see cref="NamingTemplate"/>: optional prefix/suffix inside the braces, token case sets
/// the output case, a separator in the token name replaces spaces), plus Sonarr's numbering tokens <c>{season:00}</c>,
/// <c>{episode:00}</c>, <c>{absolute:000}</c> and its multi-episode styles. Quality names are Sonarr's
/// (<c>Bluray-1080p Remux</c>), so a renamed file parses back to the same quality. Bigarr addition: <c>{Version}</c>.
/// </summary>
public static partial class SeriesNamingTemplate
{
    /// <summary>File names longer than this (in UTF-8 bytes, extension included) get their episode title shortened, as Sonarr does.</summary>
    public const int MaxFileNameBytes = 250;

    public static readonly IReadOnlyList<SeriesNamingToken> Tokens =
    [
        new("{Series Title}", "Title of the series", "The Series Title's!", SeriesTokenScope.Series),
        new("{Series TitleYear}", "Title with the year, unless the title already ends with it", "The Series Title's! (2010)", SeriesTokenScope.Series),
        new("{Series CleanTitle}", "Title without punctuation", "The Series Titles", SeriesTokenScope.Series),
        new("{Series CleanTitleYear}", "Title without punctuation, with the year", "The Series Titles 2010", SeriesTokenScope.Series),
        new("{Series TitleWithoutYear}", "Title without a trailing year", "The Series Title's!", SeriesTokenScope.Series),
        new("{Series TitleThe}", "Title with a leading article moved to the end", "Series Title's!, The", SeriesTokenScope.Series),
        new("{Series Year}", "Year the series started", "2010", SeriesTokenScope.Series),
        new("{Year}", "Same as {Series Year}", "2010", SeriesTokenScope.Series),
        new("{TvdbId}", "TVDB id; {tvdb-{TvdbId}} prints tvdb-…, or nothing without an id", "1520211", SeriesTokenScope.Series),
        new("{TmdbId}", "TMDB id; {tmdb-{TmdbId}} prints tmdb-…", "1396", SeriesTokenScope.Series),
        new("{ImdbId}", "IMDb id; {imdb-{ImdbId}} prints imdb-…", "tt0903747", SeriesTokenScope.Series),
        new("{Version}", "Name of the version", "1080p", SeriesTokenScope.Series),
        new("{season:00}", "Season number, padded to the number of zeros ({season} is not padded)", "01", SeriesTokenScope.Season),
        new("{episode:00}", "Episode number(s), padded; S{season:00}E{episode:00} follows the multi-episode style", "01", SeriesTokenScope.Episode),
        new("{absolute:000}", "Absolute episode number(s) (anime)", "001", SeriesTokenScope.Episode),
        new("{Air-Date}", "Air date; {Air Date} prints it with spaces", "2013-10-30", SeriesTokenScope.Episode),
        new("{Episode Title}", "Title of the episode(s), joined with + (shortened when the name is too long)", "Episode Title (1)", SeriesTokenScope.Episode),
        new("{Episode CleanTitle}", "Episode title without punctuation", "Episode Title 1", SeriesTokenScope.Episode),
        new("{Quality Full}", "Quality with proper and real markers", "HDTV-720p Proper", SeriesTokenScope.Episode),
        new("{Quality Title}", "Quality", "HDTV-720p", SeriesTokenScope.Episode),
        new("{Quality Proper}", "\"Proper\" for a proper or repack", "Proper", SeriesTokenScope.Episode),
        new("{Quality Real}", "\"Real\" for a real proper", "Real", SeriesTokenScope.Episode),
        new("{MediaInfo Simple}", "Video and audio codecs", "x264 DTS", SeriesTokenScope.Episode),
        new("{MediaInfo Full}", "Codecs and audio languages", "x264 DTS [EN+DE]", SeriesTokenScope.Episode),
        new("{MediaInfo VideoCodec}", "Video codec", "x264", SeriesTokenScope.Episode),
        new("{MediaInfo VideoBitDepth}", "Video bit depth", "10", SeriesTokenScope.Episode),
        new("{MediaInfo VideoDynamicRange}", "HDR or nothing", "HDR", SeriesTokenScope.Episode),
        new("{MediaInfo VideoDynamicRangeType}", "HDR flavour", "DV HDR10", SeriesTokenScope.Episode),
        new("{MediaInfo AudioCodec}", "Audio codec", "DTS", SeriesTokenScope.Episode),
        new("{MediaInfo AudioChannels}", "Audio channels", "5.1", SeriesTokenScope.Episode),
        new("{MediaInfo AudioLanguages}", "Audio languages", "[EN+DE]", SeriesTokenScope.Episode),
        new("{MediaInfo SubtitleLanguages}", "Subtitle languages", "[EN]", SeriesTokenScope.Episode),
        new("{Custom Formats}", "Matched custom formats flagged for renaming", "iNTERNAL", SeriesTokenScope.Episode),
        new("{Custom Format:Name}", "One custom format, when matched", "iNTERNAL", SeriesTokenScope.Episode),
        new("{Release Group}", "Release group", "Rls Grp", SeriesTokenScope.Episode),
        new("{Original Title}", "Release name the file came from", "The.Series.Title's!.S01E01.720p.HDTV.x264-Rls.Grp", SeriesTokenScope.Episode),
        new("{Original Filename}", "File name in the download", "the.series.title's!.s01e01.720p.hdtv.x264-rls.grp", SeriesTokenScope.Episode),
    ];

    /// <summary>Sonarr's sample series, shown by the previews.</summary>
    public static readonly SeriesNamingContext Sample = new("The Series Title's!", 2010, 1520211, 1396, "tt0903747", "1080p")
    {
        Episodes = [new EpisodeNaming(1, 1, 1, new DateOnly(2013, 10, 30), "Episode Title (1)")],
        QualityName = "HDTV-720p",
        RevisionVersion = 2,
        ReleaseGroup = "RlsGrp",
        Languages = ["English", "German"],
        MediaInfo = new MediaInfo
        {
            VideoCodec = "x264",
            VideoBitDepth = 8,
            AudioCodec = "DTS",
            AudioChannels = 5.1,
            AudioLanguages = ["English", "German"],
            Subtitles = ["English"],
        },
        CustomFormats = ["iNTERNAL"],
        OriginalReleaseTitle = "The.Series.Title's!.S01E01.Proper.720p.HDTV.x264-RlsGrp",
        OriginalFileName = "the.series.title's!.s01e01.proper.720p.hdtv.x264-rlsgrp",
    };

    /// <summary>Two episodes of one file, for the multi-episode preview.</summary>
    public static readonly SeriesNamingContext MultiSample = Sample with
    {
        Episodes = [new EpisodeNaming(1, 1, 1, new DateOnly(2013, 10, 30), "Episode Title (1)"), new EpisodeNaming(1, 2, 2, new DateOnly(2013, 10, 30), "Episode Title (2)")],
    };

    /// <summary>An anime episode, for the anime preview.</summary>
    public static readonly SeriesNamingContext AnimeSample = Sample with
    {
        SeriesTitle = "The Anime Title",
        Episodes = [new EpisodeNaming(2, 3, 15, new DateOnly(2013, 10, 30), "Episode Title")],
        QualityName = "WEBDL-1080p",
        RevisionVersion = 1,
        ReleaseGroup = "SubsPlease",
    };

    /// <summary>
    /// The series folder (frozen at version creation). Values are cleaned where they are printed, with the colon and illegal
    /// character settings of <paramref name="cleaning"/> (Sonarr's defaults when null).
    /// </summary>
    public static string RenderSeriesFolder(string template, SeriesNamingContext context, NameCleaning? cleaning = null) =>
        NameCleaner.FinishFolder(RenderTokens(template, context with { Episodes = [] }, MultiEpisodeStyle.PrefixedRange, cleaning), cleaning);

    /// <summary>A season folder; season 0 takes the specials folder name instead (which may hold tokens too).</summary>
    public static string RenderSeasonFolder(string seasonTemplate, string specialsTemplate, int seasonNumber, SeriesNamingContext context, NameCleaning? cleaning = null)
    {
        var template = seasonNumber == 0 ? specialsTemplate : seasonTemplate;
        var seasonOnly = context with { Episodes = [new EpisodeNaming(seasonNumber, 0, null, null, string.Empty)] };
        return NameCleaner.FinishSeasonFolder(RenderTokens(template, seasonOnly, MultiEpisodeStyle.PrefixedRange, cleaning, seasonOnly: true), cleaning);
    }

    /// <summary>An episode file name, without extension. Too long a name loses the end of its episode title first.</summary>
    public static string RenderFile(string template, SeriesNamingContext context, MultiEpisodeStyle style, string extension = ".mkv", NameCleaning? cleaning = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        // A literal "..." in the template survives the merge of repeated separators, as Sonarr's {ellipsis} does.
        template = template.Replace("...", NameCleaner.Ellipsis.ToString(), StringComparison.Ordinal);
        var name = NameCleaner.FinishFile(RenderTokens(template, context, style, cleaning), cleaning);
        var budget = MaxFileNameBytes - Encoding.UTF8.GetByteCount(extension);
        var overflow = Encoding.UTF8.GetByteCount(name) - budget;
        if (overflow <= 0)
        {
            return name;
        }

        var title = EpisodeTitle(context.Episodes) ?? string.Empty;
        if (title.Length > overflow + 3)
        {
            var shortened = title[..^(overflow + 3)].TrimEnd() + NameCleaner.Ellipsis;
            name = NameCleaner.FinishFile(RenderTokens(template, context, style, cleaning, titleOverride: shortened), cleaning);
        }

        // Still too long (huge series title): cut, the extension keeps its room.
        while (Encoding.UTF8.GetByteCount(name) > budget && name.Length > 1)
        {
            name = name[..^1];
        }

        return name.TrimEnd(' ', '.');
    }

    /// <summary>Which episode template a series of this type uses.</summary>
    public static string EpisodeTemplateFor(MediaManagementConfig config, bool daily, bool anime)
    {
        ArgumentNullException.ThrowIfNull(config);
        return daily ? config.DailyEpisodeTemplate : anime ? config.AnimeEpisodeTemplate : config.StandardEpisodeTemplate;
    }

    /// <summary>Empty when the template is usable for <paramref name="kind"/>.</summary>
    public static IReadOnlyList<string> Validate(string? template, SeriesTemplateKind kind)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("The template is empty.");
            return errors;
        }

        var maxScope = kind switch
        {
            SeriesTemplateKind.SeriesFolder => SeriesTokenScope.Series,
            SeriesTemplateKind.SeasonFolder or SeriesTemplateKind.SpecialsFolder => SeriesTokenScope.Season,
            _ => SeriesTokenScope.Episode,
        };

        var hasSeason = SeasonRegex().IsMatch(template);
        var hasEpisode = EpisodeRegex().IsMatch(template);
        var hasAbsolute = AbsoluteRegex().IsMatch(template);
        if (hasSeason && maxScope < SeriesTokenScope.Season)
        {
            errors.Add("{season} cannot be used in a series folder name.");
        }

        if ((hasEpisode || hasAbsolute) && maxScope < SeriesTokenScope.Episode)
        {
            errors.Add("{episode} and {absolute} depend on the file: not in a folder name.");
        }

        var stripped = NumberingStrip().Replace(template, string.Empty);
        var hasAirDate = false;
        foreach (Match match in TokenRegex().Matches(stripped))
        {
            var name = Normalize(match.Groups["token"].Value);
            if (!Scopes.TryGetValue(name, out var scope))
            {
                errors.Add($"Unknown token {{{match.Groups["token"].Value}}}.");
            }
            else if (scope > maxScope)
            {
                errors.Add($"{{{match.Groups["token"].Value}}} cannot be used in a folder name: it depends on the file.");
            }

            hasAirDate |= name == "air date";
        }

        switch (kind)
        {
            case SeriesTemplateKind.SeasonFolder when !hasSeason:
                errors.Add("The season folder must contain {season} (or {season:00}).");
                break;
            case SeriesTemplateKind.Standard when !(hasSeason && hasEpisode):
                errors.Add("The standard episode format must contain {season} and {episode}.");
                break;
            case SeriesTemplateKind.Daily when !hasAirDate && !(hasSeason && hasEpisode):
                errors.Add("The daily episode format must contain {Air-Date}, or {season} and {episode}.");
                break;
            case SeriesTemplateKind.Anime when !hasAbsolute && !(hasSeason && hasEpisode):
                errors.Add("The anime episode format must contain {absolute}, or {season} and {episode}.");
                break;
        }

        if (kind is SeriesTemplateKind.Standard or SeriesTemplateKind.Daily or SeriesTemplateKind.Anime && !NamingTemplate.HasDiscriminant(stripped))
        {
            errors.Add("The episode format must contain {Quality Full}, {Quality Title} or {Version}: two versions sharing a folder would otherwise collide.");
        }

        if (errors.Count == 0)
        {
            var rendered = kind switch
            {
                SeriesTemplateKind.SeriesFolder => RenderSeriesFolder(template, Sample),
                SeriesTemplateKind.SeasonFolder => RenderSeasonFolder(template, template, 1, Sample),
                SeriesTemplateKind.SpecialsFolder => RenderSeasonFolder(template, template, 0, Sample),
                _ => RenderFile(template, Sample, MultiEpisodeStyle.PrefixedRange),
            };
            if (rendered is "_" or "")
            {
                errors.Add("The template produces an empty name.");
            }
        }

        return errors;
    }

    /// <summary>Bigarr stores Radarr's quality names; Sonarr calls the remuxes <c>Bluray-1080p Remux</c>.</summary>
    public static string? SonarrQualityName(string? name) => name switch
    {
        null => null,
        "Remux-1080p" => "Bluray-1080p Remux",
        "Remux-2160p" => "Bluray-2160p Remux",
        _ => name,
    };

    /// <summary>
    /// Sonarr's episode title of a file: one title, or the shared title of a multi-part episode
    /// ("Pilot (1)" + "Pilot (2)" → "Pilot"), else the titles joined with " + ". Each title loses its trailing spaces, dots and
    /// question marks first (<c>EpisodeTitleTrimCharacters</c>): "Who Are You?" prints as "Who Are You", never "Who Are You!".
    /// </summary>
    public static string? EpisodeTitle(IReadOnlyList<EpisodeNaming> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        if (episodes.Count == 0)
        {
            return null;
        }

        static string Trimmed(string? title) => (title ?? string.Empty).TrimEnd(' ', '.', '?');

        if (episodes.Count == 1)
        {
            var only = Trimmed(episodes[0].Title).Trim();
            return only.Length == 0 ? null : only;
        }

        var cleaned = episodes.Select(e => MultiPartRegex().Replace(Trimmed(e.Title), string.Empty).Trim()).ToList();
        if (cleaned.All(c => c.Length > 0) && cleaned.Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return cleaned[0];
        }

        var titles = episodes.Select(e => Trimmed(e.Title).Trim()).Where(t => t.Length > 0).ToList();
        return titles.Count == 0 ? null : string.Join(" + ", titles);
    }

    private static string RenderTokens(string template, SeriesNamingContext context, MultiEpisodeStyle style, NameCleaning? cleaning, bool seasonOnly = false, string? titleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        var episodes = context.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        var text = template;

        if (!seasonOnly)
        {
            text = SeasonEpisodeRegex().Replace(text, m => SeasonEpisode(m, episodes, style));
            text = AbsoluteRegex().Replace(text, m => Absolute(m, episodes, style));
            text = EpisodeRegex().Replace(text, m => Numbers(episodes.Select(e => e.EpisodeNumber).ToList(), Pad(m), style));
        }

        text = SeasonRegex().Replace(text, m => episodes.Count == 0 ? string.Empty : episodes[0].SeasonNumber.ToString(Pad(m), CultureInfo.InvariantCulture));

        return TokenRegex().Replace(text, match =>
        {
            var token = match.Groups["token"].Value;
            var value = Resolve(Normalize(token), match.Groups["arg"].Success ? match.Groups["arg"].Value : null, context, episodes, titleOverride)?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = ApplyCase(token, value);
            var separator = match.Groups["sep"].Success ? match.Groups["sep"].Value.Trim() : string.Empty;
            if (separator.Length > 0)
            {
                value = value.Replace(' ', separator[0]);
            }

            value = NameCleaner.Clean(value, cleaning);
            return value.Length == 0 ? string.Empty : match.Groups["tag"].Value + match.Groups["prefix"].Value + value + match.Groups["suffix"].Value;
        });
    }

    private static string SeasonEpisode(Match match, List<EpisodeNaming> episodes, MultiEpisodeStyle style)
    {
        if (episodes.Count == 0)
        {
            return string.Empty;
        }

        var prefix = match.Groups["prefix"].Value;
        var seasonFormat = PadOf(match.Groups["spad"]);
        var episodeFormat = PadOf(match.Groups["epad"]);
        var marker = match.Groups["marker"].Value;
        var trail = match.Groups["trail"].Value;
        var season = episodes[0].SeasonNumber.ToString(seasonFormat, CultureInfo.InvariantCulture);
        string E(EpisodeNaming e) => e.EpisodeNumber.ToString(episodeFormat, CultureInfo.InvariantCulture);
        string Full(EpisodeNaming e) => prefix + e.SeasonNumber.ToString(seasonFormat, CultureInfo.InvariantCulture) + marker + E(e);

        if (episodes.Count == 1)
        {
            return Full(episodes[0]) + trail;
        }

        var head = prefix + season + marker;
        var body = style switch
        {
            MultiEpisodeStyle.Extend => head + string.Join("-", episodes.Select(E)),
            MultiEpisodeStyle.Duplicate => string.Join(trail.Length > 0 ? trail : " ", episodes.Select(Full)),
            MultiEpisodeStyle.Repeat => head + string.Join(marker, episodes.Select(E)),
            MultiEpisodeStyle.Scene => head + string.Join("-" + marker, episodes.Select(E)),
            MultiEpisodeStyle.Range => head + E(episodes[0]) + "-" + E(episodes[^1]),
            _ => head + E(episodes[0]) + "-" + marker + E(episodes[^1]),
        };
        return body + trail;
    }

    private static string Absolute(Match match, List<EpisodeNaming> episodes, MultiEpisodeStyle style)
    {
        var numbers = episodes.Where(e => e.AbsoluteEpisodeNumber is not null).Select(e => e.AbsoluteEpisodeNumber!.Value).ToList();
        return Numbers(numbers, Pad(match), style);
    }

    private static string Numbers(List<int> numbers, string format, MultiEpisodeStyle style)
    {
        if (numbers.Count == 0)
        {
            return string.Empty;
        }

        var printed = numbers.Select(n => n.ToString(format, CultureInfo.InvariantCulture)).ToList();
        if (printed.Count == 1)
        {
            return printed[0];
        }

        return style is MultiEpisodeStyle.Range or MultiEpisodeStyle.PrefixedRange
            ? printed[0] + "-" + printed[^1]
            : string.Join("-", printed);
    }

    private static string Pad(Match match) => PadOf(match.Groups["pad"]);

    private static string PadOf(Group group) => group.Success && group.Value.Length > 0 ? new string('0', group.Value.Length) : "0";

    private static readonly Dictionary<string, SeriesTokenScope> Scopes = Tokens
        .Where(t => !t.Name.StartsWith("{season", StringComparison.Ordinal) && !t.Name.StartsWith("{episode", StringComparison.Ordinal) && !t.Name.StartsWith("{absolute", StringComparison.Ordinal))
        .ToDictionary(t => Normalize(t.Name.Trim('{', '}').Split(':')[0]), t => t.Scope);

    private static string Normalize(string token) => SeparatorRegex().Replace(token, " ").Trim().ToLowerInvariant();

    private static string? Resolve(string name, string? argument, SeriesNamingContext c, List<EpisodeNaming> episodes, string? titleOverride) => name switch
    {
        "series title" => c.SeriesTitle,
        "series titleyear" => TitleYear(c.SeriesTitle, c.Year),
        "series cleantitle" => NamingTemplate.CleanTitle(c.SeriesTitle),
        "series cleantitleyear" => NamingTemplate.CleanTitle(TitleYear(c.SeriesTitle, c.Year)),
        "series titlewithoutyear" => TrailingYearRegex().Replace(c.SeriesTitle, string.Empty).Trim(),
        "series titlethe" => NamingTemplate.TitleThe(c.SeriesTitle),
        "series year" or "year" => c.Year is > 0 ? c.Year.Value.ToString(CultureInfo.InvariantCulture) : null,
        "tvdbid" => c.TvdbId is > 0 ? c.TvdbId.Value.ToString(CultureInfo.InvariantCulture) : null,
        "tmdbid" => c.TmdbId is > 0 ? c.TmdbId.Value.ToString(CultureInfo.InvariantCulture) : null,
        "imdbid" => c.ImdbId,
        "version" => c.VersionName,
        "air date" => episodes.FirstOrDefault()?.AirDate?.ToString("yyyy MM dd", CultureInfo.InvariantCulture),
        "episode title" => titleOverride ?? EpisodeTitle(episodes),
        "episode cleantitle" => (titleOverride ?? EpisodeTitle(episodes)) is { } t ? NamingTemplate.CleanTitle(t) : null,
        "quality title" => SonarrQualityName(c.QualityName),
        "quality full" => QualityFull(c),
        "quality proper" => c.RevisionVersion > 1 || c.IsRepack ? "Proper" : null,
        "quality real" => c.RevisionReal > 0 ? "Real" : null,
        "mediainfo simple" => Join(" ", c.MediaInfo?.VideoCodec, c.MediaInfo?.AudioCodec),
        "mediainfo full" => Join(" ", c.MediaInfo?.VideoCodec, c.MediaInfo?.AudioCodec, Languages(c.MediaInfo?.AudioLanguages)),
        "mediainfo videocodec" => c.MediaInfo?.VideoCodec,
        "mediainfo videobitdepth" => c.MediaInfo?.VideoBitDepth?.ToString(CultureInfo.InvariantCulture),
        "mediainfo videodynamicrange" => c.MediaInfo?.VideoDynamicRange,
        "mediainfo videodynamicrangetype" => c.MediaInfo?.VideoDynamicRangeType,
        "mediainfo audiocodec" => c.MediaInfo?.AudioCodec,
        "mediainfo audiochannels" => c.MediaInfo?.AudioChannels is { } channels ? channels.ToString("0.0", CultureInfo.InvariantCulture) : null,
        "mediainfo audiolanguages" => Languages(c.MediaInfo?.AudioLanguages),
        "mediainfo subtitlelanguages" => Languages(c.MediaInfo?.Subtitles),
        "custom formats" => c.CustomFormats.Count == 0 ? null : string.Join(' ', c.CustomFormats),
        "custom format" => argument is null ? null : c.CustomFormats.FirstOrDefault(f => string.Equals(f, argument.Trim(), StringComparison.OrdinalIgnoreCase)),
        "release group" => c.ReleaseGroup,
        "original title" => c.OriginalReleaseTitle,
        "original filename" => c.OriginalFileName,
        _ => null,
    };

    private static string TitleYear(string title, int? year) =>
        year is > 0 && !TrailingYearRegex().IsMatch(title) ? $"{title} ({year})" : title;

    private static string? QualityFull(SeriesNamingContext c)
    {
        var name = SonarrQualityName(c.QualityName);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (c.RevisionVersion > 1 || c.IsRepack)
        {
            name += " Proper";
        }

        if (c.RevisionReal > 0)
        {
            name += " Real";
        }

        return name;
    }

    private static string? Languages(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        return $"[{string.Join('+', names.Select(LanguageCodes.Of).Distinct())}]";
    }

    private static string? Join(string separator, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }

    private static string ApplyCase(string token, string value)
    {
        var letters = token.Where(char.IsLetter).ToList();
        if (letters.Count == 0)
        {
            return value;
        }

        if (letters.All(char.IsLower))
        {
            return value.ToLowerInvariant();
        }

        return letters.All(char.IsUpper) ? value.ToUpperInvariant() : value;
    }

    [GeneratedRegex(@"(?<prefix>s?)\{season(?::(?<spad>0+))?\}(?<marker>[- ._]?[ex])\{episode(?::(?<epad>0+))?\}(?<trail>[- ._]+?(?=\{))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"\{season(?::(?<pad>0+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"\{episode(?::(?<pad>0+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"\{absolute(?::(?<pad>0+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteRegex();

    [GeneratedRegex(@"\{(?:season|episode|absolute)(?::0+)?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberingStrip();

    [GeneratedRegex(@"(?<tag>\{(?:imdb-|tvdb-|tmdb-|edition-))?\{(?<prefix>[- ._\[(]*)(?<token>[a-z0-9]+(?:(?<sep>[- ._]+)[a-z0-9]+)*)(?::(?<arg>[^{}:]+))?(?<suffix>[-} ._)\]]*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[- ._]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s*\(\d{4}\)$")]
    private static partial Regex TrailingYearRegex();

    /// <summary>Sonarr's <c>MultiPartCleanupRegex</c>: "(1)", ": Part 2", "Pt. 3" at the end of a title.</summary>
    [GeneratedRegex(@"(?:\:?\s?(?:\(\d+\)|(?:Part|Pt\.?)\s?\d+))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MultiPartRegex();
}
