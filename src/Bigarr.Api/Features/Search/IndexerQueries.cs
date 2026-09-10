using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Search;

public sealed record IndexerQueryResult(Indexer Indexer, IReadOnlyList<ReleaseInfo> Releases, IndexerException? Error);

/// <summary>Every indexer in parallel, each with its own timeout; a failing indexer is a line in the result, never an exception.</summary>
public static class IndexerQueries
{
    public static readonly TimeSpan PerIndexerTimeout = TimeSpan.FromSeconds(60);

    public static async Task<IndexerQueryResult[]> RunAsync(
        IReadOnlyList<Indexer> indexers,
        Func<Indexer, CancellationToken, Task<IReadOnlyList<ReleaseInfo>>> query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(indexers);
        ArgumentNullException.ThrowIfNull(query);

        return await Task.WhenAll(indexers.Select(async indexer =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PerIndexerTimeout);
            try
            {
                return new IndexerQueryResult(indexer, await query(indexer, timeout.Token), null);
            }
            catch (IndexerException ex)
            {
                return new IndexerQueryResult(indexer, [], ex);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new IndexerQueryResult(indexer, [], new IndexerException(IndexerFailureKind.Unreachable, $"No answer within {PerIndexerTimeout.TotalSeconds:0} seconds."));
            }
        }));
    }
}
