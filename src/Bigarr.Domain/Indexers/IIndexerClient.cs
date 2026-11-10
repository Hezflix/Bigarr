namespace Bigarr.Domain.Indexers;

/// <summary>
/// Protocol implementation for one <see cref="IndexerImplementation"/>. Stateless: the indexer row carries the
/// configuration, the gateway in front carries rate limiting and backoff.
/// </summary>
public interface IIndexerClient
{
    IndexerImplementation Implementation { get; }

    Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>The latest releases, unfiltered: what the RSS loop consumes.</summary>
    Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken);

    /// <summary>
    /// A series search (Sonarr's request tiers): ids first, titles only when the ids find nothing. Every HTTP request
    /// waits for <paramref name="pace"/> first, so the gateway's rate limit spaces each of them.
    /// </summary>
    Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, Func<CancellationToken, Task> pace, CancellationToken cancellationToken);

    /// <summary>The latest releases of the indexer's TV and anime categories: the series side of the RSS loop.</summary>
    Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken);
}
