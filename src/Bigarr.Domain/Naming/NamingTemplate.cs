using System.Globalization;
using System.Text.RegularExpressions;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Naming;

/// <param name="Folder">Usable in the folder template as well as the file template.</param>
public sealed record NamingToken(string Name, string Description, string Example, bool Folder);

/// <summary>
/// Radarr-compatible naming templates: <c>{Movie Title} {(Release Year)} {[Quality Full]}</c>. Anything between the braces
/// around a token is a prefix or suffix printed only when the token has a value, so an absent year drops its parentheses.
/// Token case sets the output case (<c>{movie title}</c> → lowercase), a separator inside the token name replaces
/// spaces (<c>{Movie.Title}</c> → <c>The.Matrix</c>). One Bigarr addition: <c>{Version}</c>, the version name.
/// </summary>
public static partial class NamingTemplate
{
    public static readonly IReadOnlyList<NamingToken> Tokens =
    [
        new("{Movie Title}", "Title of the film", "The Matrix", true),
        new("{Movie CleanTitle}", "Title without punctuation", "The Matrix", true),
        new("{Movie TitleThe}", "Title with a leading article moved to the end", "Matrix, The", true),
        new("{Movie OriginalTitle}", "Original title", "Gisaengchung", true),
        new("{Movie CleanOriginalTitle}", "Original title without punctuation", "Gisaengchung", true),
        new("{Release Year}", "Year of the film", "1999", true),
        new("{ImdbId}", "IMDb id", "tt0133093", true),
        new("{TmdbId}", "TMDB id", "603", true),
        new("{Version}", "Name of the version", "1080p", true),
        new("{Edition Tags}", "Edition of the release", "Director's Cut", false),
        new("{Quality Full}", "Quality with proper and real markers", "Bluray-1080p Proper", false),
        new("{Quality Title}", "Quality", "Bluray-1080p", false),
        new("{Quality Proper}", "\"Proper\" for a proper or repack", "Proper", false),
        new("{Quality Real}", "\"Real\" for a real proper", "Real", false),
        new("{MediaInfo Simple}", "Video and audio codecs", "x264 DTS", false),
        new("{MediaInfo Full}", "Codecs and audio languages", "x264 DTS [EN+FR]", false),
        new("{MediaInfo VideoCodec}", "Video codec", "x265", false),
        new("{MediaInfo VideoBitDepth}", "Video bit depth", "10", false),
        new("{MediaInfo VideoDynamicRange}", "HDR or nothing", "HDR", false),
        new("{MediaInfo VideoDynamicRangeType}", "HDR flavour", "DV HDR10", false),
        new("{MediaInfo AudioCodec}", "Audio codec", "TrueHD Atmos", false),
        new("{MediaInfo AudioChannels}", "Audio channels", "7.1", false),
        new("{MediaInfo AudioLanguages}", "Audio languages", "[EN+FR]", false),
        new("{MediaInfo SubtitleLanguages}", "Subtitle languages", "[EN]", false),
        new("{Custom Formats}", "Matched custom formats flagged for renaming", "HDR10 Atmos", false),
        new("{Custom Format:Name}", "One custom format, when matched", "Atmos", false),
        new("{Release Group}", "Release group", "SPARKS", false),
        new("{Original Title}", "Release name the file came from", "The.Matrix.1999.1080p.BluRay.x264-SPARKS", false),
        new("{Original Filename}", "File name in the download", "the.matrix.1999.1080p.bluray.x264-sparks", false),
    ];

    /// <summary>The sample every preview and validation renders.</summary>
    public static readonly NamingContext Sample = new("The Matrix", "The Matrix", 1999, 603, "tt0133093", "1080p")
    {
        QualityName = "Bluray-1080p",
        RevisionVersion = 2,
        Edition = "Director's Cut",
        ReleaseGroup = "SPARKS",
        Languages = ["English", "French"],
        MediaInfo = new MediaInfo
        {
            VideoCodec = "x264",
            VideoBitDepth = 8,
            AudioCodec = "DTS",
            AudioChannels = 5.1,
            AudioLanguages = ["English", "French"],
            Subtitles = ["English"],
        },
        CustomFormats = ["HD Bluray Tier 01"],
        OriginalReleaseTitle = "The.Matrix.1999.REPACK.1080p.BluRay.x264-SPARKS",
        OriginalFileName = "the.matrix.1999.repack.1080p.bluray.x264-sparks",
    };

    /// <summary>
    /// A file name, without extension. Each value is cleaned where it is printed (<see cref="NameCleaner.Clean"/>, with the
    /// colon and illegal character settings of <paramref name="cleaning"/>, Radarr's defaults when null), then the name is
    /// finished like Radarr's file names.
    /// </summary>
    public static string Render(string template, NamingContext context, NameCleaning? cleaning = null) =>
        NameCleaner.FinishFile(RenderTokens(template, context, cleaning), cleaning);

    /// <summary>A folder name: the same values, finished like Radarr's movie folders (only dots and spaces cut at the ends).</summary>
    public static string RenderFolder(string template, NamingContext context, NameCleaning? cleaning = null) =>
        NameCleaner.FinishFolder(RenderTokens(template, context, cleaning), cleaning);

    private static string RenderTokens(string template, NamingContext context, NameCleaning? cleaning)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        return TokenRegex().Replace(template, match =>
        {
            var token = match.Groups["token"].Value;
            var value = Resolve(Normalize(token), match.Groups["arg"].Success ? match.Groups["arg"].Value : null, context)?.Trim();
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

    /// <summary>Empty when the template is usable. <paramref name="forFile"/> adds the collision rule of blueprint §3.3.</summary>
    public static IReadOnlyList<string> Validate(string? template, bool forFile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("The template is empty.");
            return errors;
        }

        foreach (Match match in TokenRegex().Matches(template))
        {
            var name = Normalize(match.Groups["token"].Value);
            if (!Known.Contains(name))
            {
                errors.Add($"Unknown token {{{match.Groups["token"].Value}}}.");
            }
            else if (!forFile && !FolderTokens.Contains(name))
            {
                errors.Add($"{{{match.Groups["token"].Value}}} cannot be used in a folder name: it depends on the file.");
            }
        }

        if (forFile && !HasDiscriminant(template))
        {
            errors.Add("The file template must contain {Quality Full}, {Quality Title} or {Version}: two versions sharing a folder would otherwise collide.");
        }

        if (errors.Count == 0 && (forFile ? Render(template, Sample) : RenderFolder(template, Sample)) is "_" or "")
        {
            errors.Add("The template produces an empty name.");
        }

        return errors;
    }

    /// <summary>Quality or version name in the template: what keeps two files of one folder apart (blueprint §3.3).</summary>
    public static bool HasDiscriminant(string template) =>
        TokenRegex().Matches(template).Select(m => Normalize(m.Groups["token"].Value)).Any(n => n is "quality full" or "quality title" or "version");

    private static readonly HashSet<string> Known = Tokens.Select(t => Normalize(t.Name.Trim('{', '}').Split(':')[0])).ToHashSet();

    private static readonly HashSet<string> FolderTokens = Tokens.Where(t => t.Folder).Select(t => Normalize(t.Name.Trim('{', '}'))).ToHashSet();

    private static string Normalize(string token) => SeparatorRegex().Replace(token, " ").Trim().ToLowerInvariant();

    private static string? Resolve(string name, string? argument, NamingContext c) => name switch
    {
        "movie title" => c.MovieTitle,
        "movie cleantitle" => CleanTitle(c.MovieTitle),
        "movie titlethe" => TitleThe(c.MovieTitle),
        "movie originaltitle" => c.OriginalTitle ?? c.MovieTitle,
        "movie cleanoriginaltitle" => CleanTitle(c.OriginalTitle ?? c.MovieTitle),
        "release year" => c.Year is > 0 ? c.Year.Value.ToString(CultureInfo.InvariantCulture) : null,
        "imdbid" => c.ImdbId,
        "tmdbid" => c.TmdbId > 0 ? c.TmdbId.ToString(CultureInfo.InvariantCulture) : null,
        "version" => c.VersionName,
        "edition tags" => c.Edition,
        "quality title" => c.QualityName,
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

    private static string? QualityFull(NamingContext c)
    {
        if (string.IsNullOrEmpty(c.QualityName))
        {
            return null;
        }

        var full = c.QualityName;
        if (c.RevisionVersion > 1 || c.IsRepack)
        {
            full += " Proper";
        }

        if (c.RevisionReal > 0)
        {
            full += " Real";
        }

        return full;
    }

    private static string? Languages(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return null;
        }

        var codes = names.Select(LanguageCodes.Of).Distinct().ToList();
        return $"[{string.Join('+', codes)}]";
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

    /// <summary>Radarr's CleanTitle: ampersand spelled out, slashes to spaces, punctuation dropped.</summary>
    public static string CleanTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var cleaned = CleanRemoveRegex().Replace(title.Replace("&", "and", StringComparison.Ordinal).Replace('/', ' '), string.Empty);
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"The Matrix" → "Matrix, The".</summary>
    public static string TitleThe(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        foreach (var article in new[] { "The ", "A ", "An " })
        {
            if (title.StartsWith(article, StringComparison.OrdinalIgnoreCase) && title.Length > article.Length)
            {
                return $"{title[article.Length..].Trim()}, {title[..(article.Length - 1)]}";
            }
        }

        return title;
    }

    [GeneratedRegex(@"(?<tag>\{(?:imdb-|edition-|tmdb-))?\{(?<prefix>[- ._\[(]*)(?<token>[a-z0-9]+(?:(?<sep>[- ._]+)[a-z0-9]+)*)(?::(?<arg>[^{}:]+))?(?<suffix>[-} ._)\]]*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[- ._]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"[,:;'""?!`|~@#$%^*=<>\[\]{}()\\]")]
    private static partial Regex CleanRemoveRegex();
}
