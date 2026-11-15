using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

/// <summary>Bound from <c>Bigarr:Discover</c>.</summary>
public sealed class DiscoverOptions
{
    public const string Section = "Bigarr:Discover";

    /// <summary>
    /// How long a TMDB answer (a list page, a title page, a genre list) is reused before TMDB is asked again. Many people
    /// browsing the same rows cost one request per row and page in this window. Availability is never cached: it is read
    /// from the library on every request.
    /// </summary>
    public int CacheMinutes { get; set; } = 15;
}

/// <summary>
/// The TMDB catalogue as Discover reads it: lists (trending, popular, upcoming, by genre, keyword, studio or network),
/// searches, title and person pages. Every answer is kept in memory for <see cref="DiscoverOptions.CacheMinutes"/>
/// to spare the TMDB quota; a title TMDB does not know is null.
/// </summary>
public sealed class TmdbDiscoverClient(HttpClient http, IOptions<MetadataOptions> metadata, IOptions<DiscoverOptions> options, IMemoryCache cache, TimeProvider time)
{
    /// <summary>TMDB serves at most 500 pages of a list.</summary>
    public const int MaxPage = 500;

    public bool Configured => metadata.Value.HasApiKey;

    private string Language => Uri.EscapeDataString(metadata.Value.Language);

    /// <summary>The certification country doubles as the region upcoming films are dated for.</summary>
    private string Region => Uri.EscapeDataString(metadata.Value.CertificationCountry);

    private string Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<TmdbPage?> TrendingAsync(int page, CancellationToken ct) => PageAsync($"trending/all/week?page={page}", ct);

    public Task<TmdbPage?> PopularMoviesAsync(int page, CancellationToken ct) => PageAsync($"movie/popular?page={page}&region={Region}", ct);

    public Task<TmdbPage?> UpcomingMoviesAsync(int page, CancellationToken ct) =>
        PageAsync($"discover/movie?page={page}&sort_by=popularity.desc&primary_release_date.gte={Today}&region={Region}&include_adult=false", ct);

    public Task<TmdbPage?> PopularSeriesAsync(int page, CancellationToken ct) => PageAsync($"tv/popular?page={page}", ct);

    public Task<TmdbPage?> UpcomingSeriesAsync(int page, CancellationToken ct) =>
        PageAsync($"discover/tv?page={page}&sort_by=popularity.desc&first_air_date.gte={Today}&include_adult=false", ct);

    /// <summary>Films with every filter given: genre, keyword, studio (TMDB "company").</summary>
    public Task<TmdbPage?> DiscoverMoviesAsync(int? genre, int? keyword, int? studio, int page, CancellationToken ct) =>
        PageAsync($"discover/movie?page={page}&sort_by=popularity.desc&include_adult=false{Filter("with_genres", genre)}{Filter("with_keywords", keyword)}{Filter("with_companies", studio)}", ct);

    /// <summary>Series with every filter given: genre, keyword, network.</summary>
    public Task<TmdbPage?> DiscoverSeriesAsync(int? genre, int? keyword, int? network, int page, CancellationToken ct) =>
        PageAsync($"discover/tv?page={page}&sort_by=popularity.desc&include_adult=false{Filter("with_genres", genre)}{Filter("with_keywords", keyword)}{Filter("with_networks", network)}", ct);

    /// <summary>Films, series and people together.</summary>
    public Task<TmdbPage?> SearchAsync(string query, int page, CancellationToken ct) =>
        PageAsync($"search/multi?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false", ct);

    public Task<TmdbPage?> SearchMoviesAsync(string query, int page, CancellationToken ct) =>
        PageAsync($"search/movie?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false", ct);

    public Task<TmdbPage?> SearchSeriesAsync(string query, int page, CancellationToken ct) =>
        PageAsync($"search/tv?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false", ct);

    public Task<TmdbDiscoverMovie?> MovieAsync(int tmdbId, CancellationToken ct) =>
        GetAsync<TmdbDiscoverMovie>($"movie/{tmdbId}?append_to_response=credits,videos,similar,recommendations,release_dates", ct);

    public Task<TmdbDiscoverTv?> SeriesAsync(int tmdbId, CancellationToken ct) =>
        GetAsync<TmdbDiscoverTv>($"tv/{tmdbId}?append_to_response=credits,videos,similar,recommendations,external_ids", ct);

    public Task<TmdbPerson?> PersonAsync(int tmdbId, CancellationToken ct) =>
        GetAsync<TmdbPerson>($"person/{tmdbId}?append_to_response=combined_credits", ct);

    public Task<TmdbGenreList?> MovieGenresAsync(CancellationToken ct) => GetAsync<TmdbGenreList>("genre/movie/list", ct);

    public Task<TmdbGenreList?> SeriesGenresAsync(CancellationToken ct) => GetAsync<TmdbGenreList>("genre/tv/list", ct);

    public Task<TmdbNamedPage?> SearchKeywordsAsync(string query, CancellationToken ct) =>
        GetAsync<TmdbNamedPage>($"search/keyword?query={Uri.EscapeDataString(query)}", ct);

    public Task<TmdbNamedPage?> SearchCompaniesAsync(string query, CancellationToken ct) =>
        GetAsync<TmdbNamedPage>($"search/company?query={Uri.EscapeDataString(query)}", ct);

    /// <summary>TMDB has no network search: a network is looked up by its id.</summary>
    public Task<TmdbNamed?> NetworkAsync(int id, CancellationToken ct) => GetAsync<TmdbNamed>($"network/{id}", ct);

    public Task<TmdbNamed?> KeywordAsync(int id, CancellationToken ct) => GetAsync<TmdbNamed>($"keyword/{id}", ct);

    public Task<TmdbNamed?> CompanyAsync(int id, CancellationToken ct) => GetAsync<TmdbNamed>($"company/{id}", ct);

    private static string Filter(string name, int? value) => value is > 0 ? $"&{name}={value.Value.ToString(CultureInfo.InvariantCulture)}" : string.Empty;

    private Task<TmdbPage?> PageAsync(string url, CancellationToken ct) => GetAsync<TmdbPage>(url, ct);

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        url += (url.Contains('?') ? "&" : "?") + "language=" + Language;
        var key = "tmdb-discover:" + url;
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached;
        }

        using var response = await http.GetAsync(url, ct);
        T? value = null;
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
            value = await response.Content.ReadFromJsonAsync<T>(TmdbClient.Json, ct);
        }

        cache.Set(key, value, TimeSpan.FromMinutes(Math.Max(1, options.Value.CacheMinutes)));
        return value;
    }
}
