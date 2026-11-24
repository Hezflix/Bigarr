using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

public sealed record IndexerQueryResult(Indexer Indexer, IReadOnlyList<ReleaseInfo> Releases, IndexerException? Error);

/// <summary>Every indexer in parallel, each with its own timeout; a failing indexer is a line in the result, never an exception.</summary>
public static class IndexerQueries
{
    /// <summary>
    /// Must stay longer than <c>TorznabClient.RequestTimeout</c> by the wait a request can spend on the indexer's rate
    /// limit. If this deadline fired first, the request would be cancelled from outside, which the gateway does not
    /// count: an indexer that never answers would cost this wait on every search and never be paused.
    /// </summary>
    public static readonly TimeSpan PerIndexerTimeout = TimeSpan.FromSeconds(90);

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

/// <summary>The indexers an interactive search asks: every one with interactive search on, or the user's pick among them.</summary>
public static class InteractiveIndexers
{
    /// <param name="indexerIds">The user's pick for this one search; null or empty asks every enabled indexer.</param>
    public static async Task<Result<List<Indexer>>> SelectAsync(BigarrDbContext db, IReadOnlyCollection<int>? indexerIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var enabled = await db.Indexers.AsNoTracking().Where(i => i.EnableInteractiveSearch).OrderBy(i => i.Priority).ToListAsync(ct);
        if (enabled.Count == 0)
        {
            return Failure.Invalid("No indexer available", "No indexer has interactive search enabled. Add one in Settings → Indexers.");
        }

        if (indexerIds is not { Count: > 0 })
        {
            return Result<List<Indexer>>.Ok(enabled);
        }

        // A pick names indexers that can be searched: one deleted or switched off since the page loaded is refused, not
        // silently dropped, so the results never claim to cover an indexer that was not asked.
        var unknown = indexerIds.Where(id => enabled.All(i => i.Id != id)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            return Failure.Invalid("Unknown indexer", $"Indexer(s) {string.Join(", ", unknown)} do not exist or have interactive search off.");
        }

        return Result<List<Indexer>>.Ok(enabled.Where(i => indexerIds.Contains(i.Id)).ToList());
    }
}
