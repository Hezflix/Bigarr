using Bigarr.Domain.Acquisition;
using Bigarr.Parsing;
using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Model;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.CustomFormats;

/// <summary>
/// What a custom format may look at: the parsed release (or file), its size, the indexer flags and the film's year and
/// original language. Built by the slice that has the data; the engine never sees a <c>Movie</c>.
/// </summary>
public sealed record CustomFormatInput(
    /// <summary>The release title with the film's title masked, as the parser produces it; group regexes never match the film name.</summary>
    string? ReleaseTitle,
    string? FileName,
    QualitySource Source,
    int Resolution,
    Modifier Modifier,
    IReadOnlyList<Language> Languages,
    Language? MovieOriginalLanguage,
    string? Edition,
    string? ReleaseGroup,
    long Size,
    IndexerFlags IndexerFlags,
    int? Year,
    /// <summary>Episodes only (Sonarr's <c>ReleaseType</c>): a single, multi-episode, season pack or multi-season pack. Films are Unknown.</summary>
    ReleaseType ReleaseType = ReleaseType.Unknown)
{
    public static CustomFormatInput FromParsed(
        ParsedMovieInfo parsed,
        string? fileName,
        long size,
        IndexerFlags indexerFlags,
        int? year,
        Language? movieOriginalLanguage)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var quality = parsed.Quality?.Quality;
        return new CustomFormatInput(
            parsed.SimpleReleaseTitle ?? parsed.ReleaseTitle,
            fileName,
            quality?.Source ?? QualitySource.UNKNOWN,
            quality?.Resolution ?? 0,
            quality?.Modifier ?? Modifier.NONE,
            EffectiveLanguages(parsed.Languages, movieOriginalLanguage),
            movieOriginalLanguage,
            string.IsNullOrWhiteSpace(parsed.Edition) ? null : parsed.Edition,
            string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup,
            size,
            indexerFlags,
            year);
    }

    /// <summary>An episode release or file (Sonarr's parser): no edition, the series' year and original language.</summary>
    public static CustomFormatInput FromEpisode(
        Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo parsed,
        string? fileName,
        long size,
        IndexerFlags indexerFlags,
        int? year,
        Language? seriesOriginalLanguage)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var quality = parsed.Quality?.Quality;
        return new CustomFormatInput(
            parsed.ReleaseTitle,
            fileName,
            quality?.Source ?? QualitySource.UNKNOWN,
            quality?.Resolution ?? 0,
            quality?.Modifier ?? Modifier.NONE,
            EffectiveLanguages(parsed.Languages, seriesOriginalLanguage),
            seriesOriginalLanguage,
            null,
            string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup,
            size,
            indexerFlags,
            year,
            ReleaseTypeOf(parsed));
    }

    /// <summary>Sonarr's release type of a parsed episode name: packs first, then several episodes, then one.</summary>
    public static ReleaseType ReleaseTypeOf(Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        if (parsed.FullSeason)
        {
            return parsed.IsMultiSeason ? ReleaseType.MultiSeasonPack : ReleaseType.SeasonPack;
        }

        var count = Math.Max(parsed.EpisodeNumbers.Length, parsed.AbsoluteEpisodeNumbers.Length);
        return count > 1 ? ReleaseType.MultiEpisode : count == 1 || parsed.IsDaily || parsed.Special ? ReleaseType.SingleEpisode : ReleaseType.Unknown;
    }

    /// <summary>A release naming no language is in the film's original language, as Radarr assumes.</summary>
    public static IReadOnlyList<Language> EffectiveLanguages(IEnumerable<Language>? parsed, Language? movieOriginalLanguage)
    {
        var languages = (parsed ?? []).Where(l => l is not null && l.Id != Language.Unknown.Id).Distinct().ToList();
        if (languages.Count == 0 && movieOriginalLanguage is not null && movieOriginalLanguage.Id != Language.Unknown.Id)
        {
            languages.Add(movieOriginalLanguage);
        }

        return languages;
    }

    /// <summary>TMDB gives ISO 639-1 codes (<c>en</c>, <c>fr</c>); the parser works with its own language list.</summary>
    public static Language? LanguageFromIsoCode(string? isoCode) =>
        string.IsNullOrWhiteSpace(isoCode) ? null : IsoLanguages.Find(isoCode.Trim())?.Language;

    /// <summary>Names as stored on files ("English", "French") back to the parser's languages.</summary>
    public static Language? LanguageFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        return Language.All.FirstOrDefault(l => string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? IsoLanguages.FindByName(trimmed)?.Language;
    }
}

/// <summary>Sonarr's <c>ReleaseType</c> values, as its <c>ReleaseTypeSpecification</c> stores them.</summary>
public enum ReleaseType
{
    Unknown = 0,
    SingleEpisode = 1,
    MultiEpisode = 2,
    SeasonPack = 3,
    MultiSeasonPack = 4,
}
