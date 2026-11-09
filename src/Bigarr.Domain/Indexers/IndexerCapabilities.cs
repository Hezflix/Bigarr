namespace Bigarr.Domain.Indexers;

/// <summary>What a Torznab endpoint advertises in <c>t=caps</c>. The mock claims everything.</summary>
public sealed record IndexerCapabilities(
    bool SupportsMovieSearch,
    bool SupportsImdbSearch,
    bool SupportsTmdbSearch,
    bool SupportsYearParameter,
    IReadOnlyList<IndexerCategory> Categories,
    int? MaxPageSize)
{
    public static IndexerCapabilities Everything { get; } = new(true, true, true, true, [], null)
    {
        SupportsTvSearch = true,
        TvSearchParams = new HashSet<string>(["q", "season", "ep", "tvdbid", "tmdbid", "imdbid"]),
    };

    /// <summary><c>tv-search available="yes"</c>.</summary>
    public bool SupportsTvSearch { get; init; }

    /// <summary>The <c>supportedParams</c> of <c>tv-search</c>, lower case (<c>q</c>, <c>season</c>, <c>ep</c>, <c>tvdbid</c>…).</summary>
    public IReadOnlySet<string> TvSearchParams { get; init; } = new HashSet<string>();

    public bool SupportsTvParam(string name) => SupportsTvSearch && TvSearchParams.Contains(name);

    /// <summary>TV categories of the caps (5000–5999) but anime, sub-categories included; empty when it lists none.</summary>
    public IReadOnlyList<int> TvCategoryIds =>
        Categories.Select(c => c.Id).Where(id => id is >= 5000 and <= 5999 && id != Indexer.AnimeCategory).Distinct().Order().ToList();

    /// <summary>The anime category, when the caps list it.</summary>
    public IReadOnlyList<int> AnimeCategoryIds =>
        Categories.Select(c => c.Id).Where(id => id == Indexer.AnimeCategory).Distinct().ToList();
}

public sealed record IndexerCategory(int Id, string Name);
