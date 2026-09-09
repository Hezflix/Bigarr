namespace Bigarr.Domain.Indexers;

/// <summary>What an indexer needs to look a film up. Identifiers first, titles as the fallback for indexers without id search.</summary>
public sealed record MovieSearchCriteria(
    string Title,
    int? Year,
    string? ImdbId,
    int? TmdbId,
    IReadOnlyList<string> AlternativeTitles)
{
    public static MovieSearchCriteria ForTitle(string title, int? year = null) => new(title, year, null, null, []);

    public IEnumerable<string> AllTitles => new[] { Title }.Concat(AlternativeTitles).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase);
}
