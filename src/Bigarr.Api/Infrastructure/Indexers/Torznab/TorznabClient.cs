using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers.Torznab;

/// <summary>
/// Torznab over HTTP. Capabilities are cached per endpoint so a search costs one request when the indexer
/// supports id lookups, one per title otherwise. Stateless apart from that cache.
/// </summary>
public sealed partial class TorznabClient(IHttpClientFactory httpClientFactory, TimeProvider time, ILogger<TorznabClient> logger) : IIndexerClient
{
    public const string HttpClientName = "torznab";

    public const int PageSize = 100;

    private static readonly TimeSpan CapabilitiesTtl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, (IndexerCapabilities Capabilities, DateTimeOffset At)> _capabilities = new();

    public IndexerImplementation Implementation => IndexerImplementation.Torznab;

    public async Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        var key = CacheKey(indexer);
        if (_capabilities.TryGetValue(key, out var cached) && time.GetUtcNow() - cached.At < CapabilitiesTtl)
        {
            return cached.Capabilities;
        }

        IndexerCapabilities capabilities;
        using (IndexerRequestTrace.Step("Capabilities"))
        {
            capabilities = await RequestAsync(indexer, new Dictionary<string, string> { ["t"] = "caps" }, null, TorznabParser.ParseCapabilities, _ => null, cancellationToken);
        }

        _capabilities[key] = (capabilities, time.GetUtcNow());
        return capabilities;
    }

    public void ForgetCapabilities(Indexer indexer) => _capabilities.TryRemove(CacheKey(indexer), out _);

    public async Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var capabilities = await GetCapabilitiesAsync(indexer, cancellationToken);
        var releases = new Dictionary<string, ReleaseInfo>(StringComparer.Ordinal);
        foreach (var query in BuildSearchQueries(capabilities, criteria))
        {
            using var step = IndexerRequestTrace.Step(query.ContainsKey("q") ? "Title" : "IDs");
            foreach (var release in await FeedAsync(indexer, query, indexer.Categories, cancellationToken))
            {
                releases.TryAdd(release.Guid, release);
            }
        }

        return releases.Values.ToList();
    }

    public async Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(indexer, cancellationToken);
        var query = new Dictionary<string, string> { ["t"] = capabilities.SupportsMovieSearch ? "movie" : "search" };
        return await FeedAsync(indexer, query, indexer.Categories, cancellationToken);
    }

    public async Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(pace);

        var capabilities = await CapabilitiesPacedAsync(indexer, pace, cancellationToken);
        var categories = SeriesQueryBuilder.Categories(indexer, capabilities, criteria.SeriesType);
        var releases = new Dictionary<string, ReleaseInfo>(StringComparer.Ordinal);
        var tiers = SeriesQueryBuilder.Build(capabilities, criteria, categories);
        for (var tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            foreach (var request in tiers[tierIndex])
            {
                using var step = IndexerRequestTrace.Step(tierIndex == 0 ? request.Step : $"Fallback: {request.Step}");
                await pace(cancellationToken);
                foreach (var release in await FeedAsync(indexer, request.Parameters, request.Categories, cancellationToken))
                {
                    releases.TryAdd(release.Guid, release);
                }
            }

            // Sonarr's tiers: the titles are only asked when the ids found nothing.
            if (releases.Count > 0)
            {
                break;
            }
        }

        return releases.Values.ToList();
    }

    public async Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        var capabilities = await GetCapabilitiesAsync(indexer, cancellationToken);
        var query = new Dictionary<string, string> { ["t"] = capabilities.SupportsTvSearch ? "tvsearch" : "search" };
        return await FeedAsync(indexer, query, SeriesQueryBuilder.FeedCategories(indexer, capabilities), cancellationToken);
    }

    private async Task<IndexerCapabilities> CapabilitiesPacedAsync(Indexer indexer, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
    {
        if (!_capabilities.TryGetValue(CacheKey(indexer), out var cached) || time.GetUtcNow() - cached.At >= CapabilitiesTtl)
        {
            await pace(cancellationToken);
        }

        return await GetCapabilitiesAsync(indexer, cancellationToken);
    }

    /// <summary>Identifiers when the endpoint accepts them, otherwise one text query per known title (three at most).</summary>
    public static IReadOnlyList<Dictionary<string, string>> BuildSearchQueries(IndexerCapabilities capabilities, MovieSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(criteria);

        if (capabilities.SupportsMovieSearch)
        {
            if (capabilities.SupportsTmdbSearch && criteria.TmdbId is > 0)
            {
                return [new Dictionary<string, string> { ["t"] = "movie", ["tmdbid"] = criteria.TmdbId.Value.ToString(CultureInfo.InvariantCulture) }];
            }

            var imdb = TorznabParser.NormalizeImdbId(criteria.ImdbId);
            if (capabilities.SupportsImdbSearch && imdb is not null)
            {
                return [new Dictionary<string, string> { ["t"] = "movie", ["imdbid"] = imdb[2..] }];
            }
        }

        var mode = capabilities.SupportsMovieSearch ? "movie" : "search";
        var queries = new List<Dictionary<string, string>>();
        foreach (var title in criteria.AllTitles.Select(CleanQuery).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
        {
            var query = new Dictionary<string, string> { ["t"] = mode, ["q"] = title };
            if (criteria.Year is > 0)
            {
                if (capabilities.SupportsYearParameter && mode == "movie")
                {
                    query["year"] = criteria.Year.Value.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    query["q"] = $"{title} {criteria.Year.Value.ToString(CultureInfo.InvariantCulture)}";
                }
            }

            queries.Add(query);
        }

        return queries;
    }

    /// <summary>Letters, digits, spaces and dashes only: punctuation confuses most trackers' text search.</summary>
    public static string CleanQuery(string title) =>
        SpacesRegex().Replace(NonQueryCharsRegex().Replace(title, " "), " ").Trim();

    public static string BuildUrl(Indexer indexer, IReadOnlyDictionary<string, string> parameters, bool includeCategories) =>
        BuildUrl(indexer, parameters, includeCategories ? indexer?.Categories ?? [] : null);

    /// <param name="categories">Null for a request without paging (<c>t=caps</c>); otherwise the categories asked, none meaning all.</param>
    public static string BuildUrl(Indexer indexer, IReadOnlyDictionary<string, string> parameters, IReadOnlyList<int>? categories)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        if (string.IsNullOrWhiteSpace(indexer.BaseUrl))
        {
            throw new IndexerException(IndexerFailureKind.Request, "The indexer has no URL.");
        }

        var baseUrl = indexer.BaseUrl.Trim().TrimEnd('/');
        var apiPath = string.IsNullOrWhiteSpace(indexer.ApiPath) ? "/api" : "/" + indexer.ApiPath.Trim().Trim('/');
        var endpoint = baseUrl.EndsWith(apiPath, StringComparison.OrdinalIgnoreCase) ? baseUrl : baseUrl + apiPath;

        var query = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            Append(query, key, value);
        }

        if (!string.IsNullOrWhiteSpace(indexer.ApiKey))
        {
            Append(query, "apikey", indexer.ApiKey.Trim());
        }

        if (categories is not null)
        {
            if (categories.Count > 0)
            {
                Append(query, "cat", string.Join(',', categories.Select(c => c.ToString(CultureInfo.InvariantCulture))));
            }

            Append(query, "limit", PageSize.ToString(CultureInfo.InvariantCulture));
            Append(query, "extended", "1");
        }

        return $"{endpoint}?{query}";
    }

    private Task<IReadOnlyList<ReleaseInfo>> FeedAsync(Indexer indexer, IReadOnlyDictionary<string, string> parameters, IReadOnlyList<int> categories, CancellationToken cancellationToken) =>
        RequestAsync(indexer, parameters, categories, xml => TorznabParser.ParseFeed(xml, indexer), releases => releases.Count, cancellationToken);

    /// <summary>One GET, its answer parsed; recorded in the search's trace (<see cref="IndexerRequestTrace"/>) when there is one.</summary>
    /// <param name="categories">Null for a request without paging (<c>t=caps</c>); otherwise the categories asked, none meaning all.</param>
    private async Task<T> RequestAsync<T>(
        Indexer indexer,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<int>? categories,
        Func<string, T> parse,
        Func<T, int?> count,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        string? url = null;
        int? status = null;
        try
        {
            url = BuildUrl(indexer, parameters, categories);
            logger.LogDebug("Torznab {Indexer}: GET {Url}", indexer.Name, Redact(url));
            var http = httpClientFactory.CreateClient(HttpClientName);
            string xml;
            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                status = (int)response.StatusCode;
                HttpFailures.ThrowIfFailed(response, "the indexer");
                xml = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new IndexerException(IndexerFailureKind.Unreachable, $"Could not reach the indexer: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IndexerException(IndexerFailureKind.Unreachable, $"The indexer did not answer within {http.Timeout.TotalSeconds:0} seconds.", ex);
            }

            var result = parse(xml);
            IndexerRequestTrace.Record(indexer, parameters, categories, url, status, time.GetElapsedTime(started), count(result), null);
            return result;
        }
        catch (IndexerException ex)
        {
            IndexerRequestTrace.Record(indexer, parameters, categories, url, status, time.GetElapsedTime(started), null, ex);
            throw;
        }
        catch (OperationCanceledException)
        {
            // The search's own deadline for this indexer, or the user leaving: recorded, then left to the caller.
            IndexerRequestTrace.Record(indexer, parameters, categories, url, status, time.GetElapsedTime(started), null,
                new IndexerException(IndexerFailureKind.Unreachable, "Cancelled: no answer before the search's deadline."));
            throw;
        }
    }

    private static void Append(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }

        query.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static string CacheKey(Indexer indexer) => $"{indexer.BaseUrl?.Trim().TrimEnd('/')}|{indexer.ApiPath}|{indexer.ApiKey}";

    /// <summary>The API key (and any other credential in the URL) never reaches the logs.</summary>
    public static string Redact(string url) => IndexerRequestTrace.Redact(url);

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s-]")]
    private static partial Regex NonQueryCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
