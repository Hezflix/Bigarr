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

        var xml = await GetAsync(indexer, new Dictionary<string, string> { ["t"] = "caps" }, includeCategories: false, cancellationToken);
        var capabilities = TorznabParser.ParseCapabilities(xml);
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
            var xml = await GetAsync(indexer, query, includeCategories: true, cancellationToken);
            foreach (var release in TorznabParser.ParseFeed(xml, indexer))
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
        var xml = await GetAsync(indexer, query, includeCategories: true, cancellationToken);
        return TorznabParser.ParseFeed(xml, indexer);
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

    public static string BuildUrl(Indexer indexer, IReadOnlyDictionary<string, string> parameters, bool includeCategories)
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

        if (includeCategories)
        {
            if (indexer.Categories.Count > 0)
            {
                Append(query, "cat", string.Join(',', indexer.Categories.Select(c => c.ToString(CultureInfo.InvariantCulture))));
            }

            Append(query, "limit", PageSize.ToString(CultureInfo.InvariantCulture));
            Append(query, "extended", "1");
        }

        return $"{endpoint}?{query}";
    }

    private async Task<string> GetAsync(Indexer indexer, IReadOnlyDictionary<string, string> parameters, bool includeCategories, CancellationToken cancellationToken)
    {
        var url = BuildUrl(indexer, parameters, includeCategories);
        logger.LogDebug("Torznab {Indexer}: GET {Url}", indexer.Name, Redact(url));

        var http = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            Prowlarr.HttpFailures.ThrowIfFailed(response, "the indexer");
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IndexerException(IndexerFailureKind.Unreachable, $"Could not reach the indexer: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IndexerException(IndexerFailureKind.Unreachable, $"The indexer did not answer within {http.Timeout.TotalSeconds:0} seconds.", ex);
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

    /// <summary>The API key never reaches the logs.</summary>
    public static string Redact(string url) => ApiKeyRegex().Replace(url, "apikey=***");

    [GeneratedRegex(@"apikey=[^&]*", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s-]")]
    private static partial Regex NonQueryCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
