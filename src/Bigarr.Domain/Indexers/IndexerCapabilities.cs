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
    public static IndexerCapabilities Everything { get; } = new(true, true, true, true, [], null);
}

public sealed record IndexerCategory(int Id, string Name);
