using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

/// <summary>Thin typed client over TMDB API v3. One request per movie thanks to <c>append_to_response</c>.</summary>
public sealed class TmdbClient(HttpClient http, IOptions<MetadataOptions> options)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<TmdbMovieDetails?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var url = $"movie/{tmdbId}?append_to_response=alternative_titles,release_dates,external_ids&language={Uri.EscapeDataString(options.Value.Language)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbMovieDetails>(Json, cancellationToken);
    }

    public async Task<TmdbFindResponse?> FindByImdbIdAsync(string imdbId, CancellationToken cancellationToken)
    {
        var url = $"find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id&language={Uri.EscapeDataString(options.Value.Language)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbFindResponse>(Json, cancellationToken);
    }

    public async Task<TmdbSearchResponse?> SearchMoviesAsync(string query, int? year, CancellationToken cancellationToken)
    {
        var url = $"search/movie?query={Uri.EscapeDataString(query)}&include_adult=false&language={Uri.EscapeDataString(options.Value.Language)}";
        if (year is > 0)
        {
            url += $"&year={year}";
        }

        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbSearchResponse>(Json, cancellationToken);
    }
}
