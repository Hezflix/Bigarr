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
}
