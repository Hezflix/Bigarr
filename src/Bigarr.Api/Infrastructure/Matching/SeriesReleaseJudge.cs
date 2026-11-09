using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes.Model;
using Bigarr.Parsing.Episodes;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>A series release judged for one series: what it maps to, why not when it does not, and each version's verdict.</summary>
public sealed record SeriesJudgement(
    ParsedEpisodeInfo? Parsed,
    IReadOnlyList<Episode> Episodes,
    /// <summary>Why the release maps to no episode of the series; null when it maps.</summary>
    string? UnmatchedReason,
    IReadOnlyList<CustomFormatDefinition> Formats,
    IReadOnlyList<ReleaseDecision> Decisions,
    /// <summary>The single version it goes to when one accepts it (blueprint §3.3), as for films.</summary>
    ReleaseDecision? Attributed)
{
    public bool IsMapped => UnmatchedReason is null && Episodes.Count > 0;
}

/// <summary>
/// Release → episodes → verdicts, shared by the interactive search and RSS: Sonarr's episode parser, S2's mapper (TVDB
/// numbering first, XEM scene numbers for a release of a scene-mapped show, absolute numbers for anime, air dates for
/// daily shows), then every version of the series judges the episodes the release covers. An unmapped or ambiguous
/// release is spelled out, never guessed.
/// </summary>
public static class SeriesReleaseJudge
{
    public static ParsedEpisodeInfo? TryParse(string title)
    {
        try
        {
            return EpisodeParser.ParseTitle(title);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <param name="sceneSeasonNumber">The scene season the release's series title stands for (an XEM name like "Show S2").</param>
    public static SeriesJudgement Judge(
        ReleaseInfo release,
        ParsedEpisodeInfo? parsed,
        SeriesTargets targets,
        int? sceneSeasonNumber,
        CustomFormatCalculator calculator,
        ReleaseEvaluator evaluator,
        int minimumSeeders,
        EpisodeSearchScope? scope = null,
        int? versionId = null,
        bool sceneSource = true)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(evaluator);

        if (parsed is null)
        {
            return new SeriesJudgement(null, [], "Release title could not be parsed as an episode", [], [], null);
        }

        var series = targets.Series;
        var formats = calculator.Match(CustomFormatScorer.EpisodeReleaseInput(parsed, release, series.Year, series.OriginalLanguage));
        var mapping = EpisodeMapper.Map(parsed, targets.Mappable, targets.Episodes.ToList(), sceneSource, sceneSource ? sceneSeasonNumber : null);
        if (!mapping.IsMapped)
        {
            return new SeriesJudgement(parsed, [], Describe(mapping), formats, [], null);
        }

        var candidate = new ReleaseCandidate(release, parsed, minimumSeeders)
        {
            CustomFormatIds = formats.Select(f => f.Id).ToList(),
            Search = scope,
        };
        var decisions = targets.ContextsFor(mapping.Episodes, versionId).Select(context => evaluator.Evaluate(candidate, context)).ToList();
        return new SeriesJudgement(parsed, mapping.Episodes, null, formats, decisions, VersionAttribution.Choose(decisions));
    }

    /// <summary>
    /// Sonarr's <c>ParseSpecialEpisodeTitle</c>: a release whose name carries no episode number (or only a possible
    /// special) is matched to a special of the series by episode title ("Show Pilot Recut 1080p" → S00E03). An
    /// <c>SxxE00</c> name that XEM maps stays as parsed. Null when no special's title is in the release name. The
    /// numbers are TVDB's, so the result must be mapped without scene numbering.
    /// </summary>
    public static ParsedEpisodeInfo? ParseSpecial(ParsedEpisodeInfo? parsed, string releaseTitle, SeriesTargets targets)
    {
        ArgumentNullException.ThrowIfNull(releaseTitle);
        ArgumentNullException.ThrowIfNull(targets);

        var series = targets.Series;
        if (parsed is { IsPossibleSceneSeasonSpecial: true } && series.UseSceneNumbering
            && targets.Episodes.Any(e => e.SceneSeasonNumber == parsed.SeasonNumbers[0] && e.SceneEpisodeNumber == 0))
        {
            return null; // XEM maps it: the regular mapping handles it
        }

        var episode = FindSpecialByTitle(targets.Episodes.Where(e => e.SeasonNumber == 0), releaseTitle);
        if (episode is null)
        {
            return null;
        }

        return new ParsedEpisodeInfo
        {
            ReleaseTitle = releaseTitle,
            SeriesTitle = series.Title,
            SeriesTitleInfo = new SeriesTitleInfo { Title = series.Title },
            SeasonNumbers = [0],
            EpisodeNumbers = [episode.EpisodeNumber],
            FullSeason = false,
            Quality = Bigarr.Parsing.Episodes.QualityParser.ParseQuality(releaseTitle),
            ReleaseGroup = Bigarr.Parsing.Episodes.ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
            Languages = Bigarr.Parsing.Episodes.LanguageParser.ParseLanguages(releaseTitle),
            Special = true,
        };
    }

    /// <summary>Sonarr's <c>EpisodeService.FindEpisodeByTitle</c>: the episode title found earliest in the name, the longest first.</summary>
    public static Episode? FindSpecialByTitle(IEnumerable<Episode> specials, string releaseTitle)
    {
        var normalized = EpisodeParser.NormalizeEpisodeTitle(releaseTitle);
        var clean = normalized.CleanSeriesTitle();
        return specials
            .Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .SelectMany(e =>
            {
                var title = EpisodeParser.NormalizeEpisodeTitle(e.Title);
                return new[]
                {
                    (Position: normalized.IndexOf(title, StringComparison.OrdinalIgnoreCase), title.Length, Episode: e),
                    (Position: clean.IndexOf(title.CleanSeriesTitle(), StringComparison.OrdinalIgnoreCase), title.Length, Episode: e),
                };
            })
            .Where(m => m.Length > 0 && m.Position >= 0)
            .OrderBy(m => m.Position)
            .ThenByDescending(m => m.Length)
            .Select(m => m.Episode)
            .FirstOrDefault();
    }

    public static string Describe(EpisodeMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        var what = mapping.Status switch
        {
            EpisodeMappingStatus.NotParsable => "No episode in the release name",
            EpisodeMappingStatus.EpisodeNotFound => "Episode not found in the series",
            EpisodeMappingStatus.Ambiguous => "Ambiguous episode, not guessed",
            EpisodeMappingStatus.WrongSeriesType => "Numbering does not fit the series type",
            _ => "Not mapped",
        };
        return mapping.Detail is null ? what : $"{what}: {mapping.Detail}";
    }

    public static string Label(IReadOnlyList<Episode> episodes) =>
        EpisodeCoverage.Describe(episodes.Select(e => new EpisodeSlot(null!, e.Id, e.SeasonNumber, e.EpisodeNumber, null, null, null, false)).ToList());
}
