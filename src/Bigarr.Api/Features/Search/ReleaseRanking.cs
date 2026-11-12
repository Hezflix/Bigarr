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
}
