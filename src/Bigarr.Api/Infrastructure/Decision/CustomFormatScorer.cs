using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Quality;
using Bigarr.Parsing;
using Bigarr.Parsing.Model;
using Microsoft.EntityFrameworkCore;
using ParserModifier = Bigarr.Parsing.Qualities.Modifier;
using ParserSource = Bigarr.Parsing.Qualities.QualitySource;

namespace Bigarr.Api.Infrastructure.Decision;

/// <summary>What a file is worth for its version's profile, computed at read time (blueprint §3.6).</summary>
public sealed record FileAssessment(IReadOnlyList<string> CustomFormats, IReadOnlyList<int> CustomFormatIds, int CustomFormatScore, bool CutoffMet);

/// <summary>
/// Loads the custom formats once per request scope and evaluates releases and files against them. Nothing is stored:
/// editing a format changes the next search or page (§3.6). Knows no <c>Movie</c>; slices pass year and language.
/// </summary>
public sealed class CustomFormatScorer(BigarrDbContext db, ILogger<CustomFormatScorer> logger)
{
    private CustomFormatCalculator? _calculator;
    private Dictionary<int, string>? _names;
    private List<CustomFormat>? _formats;
    private string? _signature;

    public async Task<CustomFormatCalculator> CalculatorAsync(CancellationToken ct)
    {
        if (_calculator is not null)
        {
            return _calculator;
        }

        var formats = await db.CustomFormats.AsNoTracking().OrderBy(f => f.Id).ToListAsync(ct);
        var definitions = new List<CustomFormatDefinition>(formats.Count);
        foreach (var format in formats)
        {
            try
            {
                definitions.Add(ToDefinition(format));
            }
            catch (CustomFormatParseException ex)
            {
                // One corrupt row must not break every search; the custom formats page shows it.
                logger.LogWarning("Custom format {Name} ({Id}) is not readable and is ignored: {Message}", format.Name, format.Id, ex.Message);
            }
        }

        _calculator = new CustomFormatCalculator(definitions);
        _names = definitions.ToDictionary(d => d.Id, d => d.Name);
        _formats = formats;
        return _calculator;
    }

    public async Task<IReadOnlyDictionary<int, string>> NamesAsync(CancellationToken ct)
    {
        await CalculatorAsync(ct);
        return _names!;
    }

    /// <summary>
    /// Identifies the custom formats as matched: their ids and specifications. Adding, removing or editing a format
    /// changes it, a rename does not (a match never depends on the name). <see cref="FileFormatMatches"/> checks it.
    /// </summary>
    public async Task<string> SignatureAsync(CancellationToken ct)
    {
        await CalculatorAsync(ct);
        if (_signature is null)
        {
            var text = new StringBuilder();
            foreach (var format in _formats!)
            {
                text.Append(format.Id).Append('\u001f').Append(format.SpecificationsJson).Append('\u001e');
            }

            _signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        return _signature;
    }

    public async Task<IReadOnlyList<CustomFormatDefinition>> MatchAsync(CustomFormatInput input, CancellationToken ct) =>
        (await CalculatorAsync(ct)).Match(input);

    public static CustomFormatDefinition ToDefinition(CustomFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new CustomFormatDefinition(
            format.Id,
            format.Name,
            format.IncludeWhenRenaming,
            CustomFormatJson.ParseSpecifications(format.SpecificationsJson, format.Name),
            format.TrashId,
            CustomFormatJson.ParseScores(format.TrashScoresJson));
    }

    public static CustomFormatInput ReleaseInput(ParsedMovieInfo parsed, ReleaseInfo release, int? year, string? originalLanguageIso)
    {
        ArgumentNullException.ThrowIfNull(release);
        return CustomFormatInput.FromParsed(parsed, null, release.Size, release.Flags, year, CustomFormatInput.LanguageFromIsoCode(originalLanguageIso));
    }

    public static CustomFormatInput EpisodeReleaseInput(Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo parsed, ReleaseInfo release, int? year, string? originalLanguageIso)
    {
        ArgumentNullException.ThrowIfNull(release);
        return CustomFormatInput.FromEpisode(parsed, null, release.Size, release.Flags, year, CustomFormatInput.LanguageFromIsoCode(originalLanguageIso));
    }

    /// <summary>
    /// An episode file (or a shadow pick standing in for one): what was stored wins over what Sonarr's parser reads from
    /// the original release name or the file name.
    /// </summary>
    public static CustomFormatInput EpisodeFileInput(
        string? originalReleaseTitle,
        string relativePath,
        long size,
        IndexerFlags flags,
        QualityDefinition? quality,
        IEnumerable<string> languageNames,
        string? releaseGroup,
        int? year,
        string? originalLanguageIso)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(languageNames);

        var fileName = Path.GetFileName(relativePath);
        var title = string.IsNullOrWhiteSpace(originalReleaseTitle) ? Path.GetFileNameWithoutExtension(relativePath) : originalReleaseTitle;
        Bigarr.Parsing.Episodes.Model.ParsedEpisodeInfo? parsed = null;
        try
        {
            parsed = Bigarr.Parsing.Episodes.Parser.ParseTitle(title);
        }
        catch (Exception)
        {
            // an unparseable name still has a quality and a size to judge
        }

        var original = CustomFormatInput.LanguageFromIsoCode(originalLanguageIso);
        var stored = languageNames.Select(CustomFormatInput.LanguageFromName).Where(l => l is not null).Select(l => l!).ToList();
        var parsedQuality = parsed?.Quality?.Quality;

        return new CustomFormatInput(
            parsed?.ReleaseTitle ?? title,
            fileName,
            quality is not null ? (ParserSource)(int)quality.Source : parsedQuality?.Source ?? ParserSource.UNKNOWN,
            quality?.Resolution ?? parsedQuality?.Resolution ?? 0,
            quality is not null ? (ParserModifier)(int)quality.Modifier : parsedQuality?.Modifier ?? ParserModifier.NONE,
            CustomFormatInput.EffectiveLanguages(stored.Count > 0 ? stored : parsed?.Languages, original),
            original,
            null,
            releaseGroup ?? (string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup),
            size,
            flags,
            year);
    }

    /// <summary>
    /// A file on disk: what the import stored wins over what the parser reads from the name, the name fills the gaps.
    /// </summary>
    public static CustomFormatInput FileInput(
        string? originalReleaseTitle,
        string relativePath,
        long size,
        IndexerFlags flags,
        QualityDefinition? quality,
        IEnumerable<string> languageNames,
        string? edition,
        string? releaseGroup,
        int? year,
        string? originalLanguageIso)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(languageNames);

        var fileName = Path.GetFileName(relativePath);
        var title = string.IsNullOrWhiteSpace(originalReleaseTitle) ? Path.GetFileNameWithoutExtension(relativePath) : originalReleaseTitle;
        ParsedMovieInfo? parsed = null;
        try
        {
            parsed = Parser.ParseMovieTitle(title);
        }
        catch (Exception)
        {
            // an unparseable name still has a quality and a size to judge
        }

        var original = CustomFormatInput.LanguageFromIsoCode(originalLanguageIso);
        var stored = languageNames.Select(CustomFormatInput.LanguageFromName).Where(l => l is not null).Select(l => l!).ToList();
        var parsedQuality = parsed?.Quality?.Quality;

        return new CustomFormatInput(
            parsed?.SimpleReleaseTitle ?? title,
            fileName,
            quality is not null ? (ParserSource)(int)quality.Source : parsedQuality?.Source ?? ParserSource.UNKNOWN,
            quality?.Resolution ?? parsedQuality?.Resolution ?? 0,
            quality is not null ? (ParserModifier)(int)quality.Modifier : parsedQuality?.Modifier ?? ParserModifier.NONE,
            CustomFormatInput.EffectiveLanguages(stored.Count > 0 ? stored : parsed?.Languages, original),
            original,
            edition ?? (string.IsNullOrWhiteSpace(parsed?.Edition) ? null : parsed!.Edition),
            releaseGroup ?? (string.IsNullOrWhiteSpace(parsed?.ReleaseGroup) ? null : parsed!.ReleaseGroup),
            size,
            flags,
            year);
    }

    public static FileAssessment Assess(QualityProfile profile, int fileQualityId, IReadOnlyList<CustomFormatDefinition> matched)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(matched);

        var ids = matched.Select(m => m.Id).ToList();
        var score = ProfileRanking.FormatScore(profile, ids);
        var cutoffMet = ProfileRanking.CutoffMet(profile, fileQualityId) && score >= profile.CutoffFormatScore;
        return new FileAssessment(matched.Select(m => m.Name).ToList(), ids, score, cutoffMet);
    }
}
