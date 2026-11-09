using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Decision;
using Bigarr.Decision.CustomFormats;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes.Model;
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
        int? versionId = null)
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
        var mapping = EpisodeMapper.Map(parsed, targets.Mappable, targets.Episodes.ToList(), sceneSource: true, sceneSeasonNumber);
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
