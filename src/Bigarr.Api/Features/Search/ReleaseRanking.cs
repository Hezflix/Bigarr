using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>Ranking: quality, then custom format score, then proper/repack, then indexer priority, then seeders.</summary>
public static class ReleaseRanking
{
    public static readonly IReadOnlyDictionary<int, int> QualityWeights = ParserQuality.DefaultQualityDefinitions.ToDictionary(d => d.Quality.Id, d => d.Weight);

    public static int WeightOf(int qualityId) => QualityWeights.GetValueOrDefault(qualityId);

    public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> releases, Func<T, int> qualityId, Func<T, int> customFormatScore, Func<T, int> revision, Func<T, int> indexerPriority, Func<T, int> seeders) =>
        releases
            .OrderByDescending(r => WeightOf(qualityId(r)))
            .ThenByDescending(customFormatScore)
            .ThenByDescending(revision)
            .ThenBy(indexerPriority)
            .ThenByDescending(seeders);

    /// <summary>
    /// The series order of the automatic paths (RSS, automatic search), Sonarr's prioritisation: quality, custom format
    /// score, then the season's own release group (<see cref="Bigarr.Decision.SeasonGroups"/>: it only breaks a tie),
    /// revision, season packs first, fewer episodes (more for anime), indexer priority, seeders.
    /// </summary>
    public static IOrderedEnumerable<T> OrderSeries<T>(
        IEnumerable<T> releases,
        Func<T, int> qualityId,
        Func<T, int> customFormatScore,
        Func<T, bool> seasonGroup,
        Func<T, int> revision,
        Func<T, bool> fullSeason,
        Func<T, int> episodeCount,
        Func<T, bool> anime,
        Func<T, int> indexerPriority,
        Func<T, int> seeders) =>
        releases
            .OrderByDescending(r => WeightOf(qualityId(r)))
            .ThenByDescending(customFormatScore)
            .ThenByDescending(seasonGroup)
            .ThenByDescending(revision)
            .ThenByDescending(fullSeason)
            .ThenBy(r => anime(r) ? -episodeCount(r) : episodeCount(r))
            .ThenBy(indexerPriority)
            .ThenByDescending(seeders);
}
