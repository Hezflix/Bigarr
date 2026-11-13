using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Search;

/// <summary>One request sent to an indexer during an interactive search, its URL redacted on the server.</summary>
public sealed record IndexerRequestResponse(
    /// <summary>Where it sits in the search: <c>IDs</c>, <c>Fallback: Title</c>, <c>Season 3 (series fallback) › IDs</c>…</summary>
    string Step,
    /// <summary>The Torznab/Newznab function (<c>t</c>): <c>search</c>, <c>tvsearch</c>, <c>movie</c>, <c>caps</c>; empty when skipped.</summary>
    string Function,
    /// <summary>The parameters asked, <c>t</c> and the API key aside.</summary>
    Dictionary<string, string> Parameters,
    /// <summary>The full URL, the API key and any other credential replaced by <c>•••</c>.</summary>
    string? Url,
    int? StatusCode,
    long DurationMs,
    /// <summary>Time waited for the indexer's rate limit before sending.</summary>
    long WaitedMs,
    /// <summary>Releases in the answer; null when the request failed.</summary>
    int? Results,
    IndexerFailureKind? ErrorKind,
    string? Error,
    /// <summary>Not sent: the indexer is paused by its backoff.</summary>
    bool Skipped);

/// <summary>What one indexer was asked during an interactive search, and what became of its answers.</summary>
public sealed record IndexerQueryReport(
    int IndexerId,
    string Indexer,
    List<IndexerRequestResponse> Requests,
    /// <summary>Distinct releases of this indexer in the results.</summary>
    int Releases,
    /// <summary>Those whose title parses.</summary>
    int Parsed,
    /// <summary>Those that are the film or the series asked.</summary>
    int Matched,
    /// <summary>Those at least one version accepts.</summary>
    int Accepted);

/// <summary>The advanced view of the interactive search: the requests of a <see cref="IndexerRequestTrace"/>, per indexer.</summary>
public static class SearchQueryReports
{
    /// <param name="outcomes">Per release kept: its indexer, whether it parsed, matched and was accepted.</param>
    public static List<IndexerQueryReport> Build(
        IndexerRequestTrace trace,
        IReadOnlyList<Indexer> indexers,
        IEnumerable<(int IndexerId, bool Parsed, bool Matched, bool Accepted)> outcomes)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(indexers);
        ArgumentNullException.ThrowIfNull(outcomes);

        var records = trace.Records.ToLookup(r => r.IndexerId);
        var kept = outcomes.ToLookup(o => o.IndexerId);
        return indexers
            .Select(indexer => new IndexerQueryReport(
                indexer.Id,
                indexer.Name,
                records[indexer.Id]
                    .Select(r => new IndexerRequestResponse(
                        r.Step,
                        r.Function,
                        new Dictionary<string, string>(r.Parameters, StringComparer.Ordinal),
                        r.Url,
                        r.StatusCode,
                        r.DurationMs,
                        r.WaitedMs,
                        r.Results,
                        r.ErrorKind,
                        r.Error,
                        r.Skipped))
                    .ToList(),
                kept[indexer.Id].Count(),
                kept[indexer.Id].Count(o => o.Parsed),
                kept[indexer.Id].Count(o => o.Matched),
                kept[indexer.Id].Count(o => o.Accepted)))
            .ToList();
    }
}
