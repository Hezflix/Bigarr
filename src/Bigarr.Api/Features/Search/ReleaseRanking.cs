using ParserQuality = Bigarr.Parsing.Qualities.Quality;

namespace Bigarr.Api.Features.Search;

/// <summary>
/// Ranking: quality, then custom format score, then proper/repack, then indexer priority, then seeders. Quality is the
/// rank of the release's quality in the target's profile (qualities grouped in the profile tie), as Radarr and Sonarr rank.
/// </summary>
public static class ReleaseRanking
{
    public static readonly IReadOnlyDictionary<int, int> QualityWeights = ParserQuality.DefaultQualityDefinitions.ToDictionary(d => d.Quality.Id, d => d.Weight);

    /// <summary>The parser's fixed weight of a quality. For display order only (interactive searches); the automatic paths rank by the profile.</summary>
    public static int WeightOf(int qualityId) => QualityWeights.GetValueOrDefault(qualityId);

    /// <param name="releases">The releases to rank.</param>
    /// <param name="qualityRank">The rank of a release's quality in the target's profile (<see cref="Bigarr.Decision.ProfileRanking.RankOf"/>); a higher rank is better.</param>
    /// <param name="customFormatScore">The release's custom format score.</param>
    /// <param name="revision">The proper/repack revision.</param>
    /// <param name="indexerPriority">The indexer priority; lower is better.</param>
    /// <param name="seeders">The seeder count.</param>
    public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> releases, Func<T, int> qualityRank, Func<T, int> customFormatScore, Func<T, int> revision, Func<T, int> indexerPriority, Func<T, int> seeders) =>
        releases
            .OrderByDescending(qualityRank)
            .ThenByDescending(customFormatScore)
            .ThenByDescending(revision)
            .ThenBy(indexerPriority)
            .ThenByDescending(seeders);

    /// <summary>
    /// The series order of the automatic paths (RSS, automatic search), Sonarr's prioritisation: quality (the rank in the
    /// target's profile, <paramref name="qualityRank"/>), custom format score, then the season's own release group (<see cref="Bigarr.Decision.SeasonGroups"/>: it only breaks a tie),
    /// revision, season packs first, fewer episodes (more for anime), indexer priority, seeders.
    /// </summary>
    public static IOrderedEnumerable<T> OrderSeries<T>(
        IEnumerable<T> releases,
        Func<T, int> qualityRank,
        Func<T, int> customFormatScore,
        Func<T, bool> seasonGroup,
        Func<T, int> revision,
        Func<T, bool> fullSeason,
        Func<T, int> episodeCount,
        Func<T, bool> anime,
        Func<T, int> indexerPriority,
        Func<T, int> seeders) =>
        releases
            .OrderByDescending(qualityRank)
            .ThenByDescending(customFormatScore)
            .ThenByDescending(seasonGroup)
            .ThenByDescending(revision)
            .ThenByDescending(fullSeason)
            .ThenBy(r => anime(r) ? -episodeCount(r) : episodeCount(r))
            .ThenBy(indexerPriority)
            .ThenByDescending(seeders);

    /// <summary>
    /// Whether the season's group decided the choice of <paramref name="chosen"/>: it realigns files of another group, or
    /// it beat a release of another group that was equal on quality and custom format score for some of the same
    /// episodes. Null when the group played no part.
    /// </summary>
    public static Downloads.SeasonGroupChoice? SeasonGroupChoice<T>(
        T chosen,
        IEnumerable<T> ranked,
        Func<T, Bigarr.Decision.EpisodeCoverage?> coverage,
        Func<T, string?> releaseGroup,
        Func<T, int> qualityId,
        Func<T, int> customFormatScore,
        Func<T, IEnumerable<int>> episodeIds)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        if (coverage(chosen) is not { } covered || !Bigarr.Decision.SeasonGroups.Matches(releaseGroup(chosen), covered))
        {
            return null;
        }

        var group = covered.SeasonGroups[covered.Slots[0].SeasonNumber];
        if (Bigarr.Decision.SeasonGroups.Realigns(releaseGroup(chosen), covered))
        {
            return new Downloads.SeasonGroupChoice(group, true);
        }

        var weight = WeightOf(qualityId(chosen));
        var score = customFormatScore(chosen);
        var episodes = episodeIds(chosen).ToHashSet();
        var beat = ranked.Any(other =>
            !EqualityComparer<T>.Default.Equals(other, chosen)
            && WeightOf(qualityId(other)) == weight
            && customFormatScore(other) == score
            && !Bigarr.Decision.SeasonGroups.Same(releaseGroup(other), group)
            && episodeIds(other).Any(episodes.Contains));
        return beat ? new Downloads.SeasonGroupChoice(group, false) : null;
    }
}
