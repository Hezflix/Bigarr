using System.Globalization;
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

    // ---- TV ---------------------------------------------------------------------------------------------------------

    /// <summary>TMDB caps <c>append_to_response</c> at 20 items.</summary>
    public const int MaxAppends = 20;

    /// <summary>Show details with its external ids (the TVDB bridge) and alternative titles.</summary>
    public async Task<TmdbTvDetails?> GetTvAsync(int tmdbId, CancellationToken cancellationToken)
    {
        var url = $"tv/{tmdbId}?append_to_response=external_ids,alternative_titles&language={Uri.EscapeDataString(options.Value.Language)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbTvDetails>(Json, cancellationToken);
    }

    /// <summary>Seasons with their episodes, 20 per request through <c>append_to_response=season/N</c>.</summary>
    public async Task<IReadOnlyList<TmdbTvSeason>> GetTvSeasonsAsync(int tmdbId, IEnumerable<int> seasonNumbers, CancellationToken cancellationToken)
    {
        var seasons = new List<TmdbTvSeason>();
        foreach (var chunk in seasonNumbers.Distinct().Order().Chunk(MaxAppends))
        {
            var appends = string.Join(',', chunk.Select(n => "season/" + n.ToString(CultureInfo.InvariantCulture)));
            var url = $"tv/{tmdbId}?append_to_response={appends}&language={Uri.EscapeDataString(options.Value.Language)}";
            using var response = await http.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return seasons;
            }

            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            foreach (var number in chunk)
            {
                if (document.RootElement.TryGetProperty("season/" + number.ToString(CultureInfo.InvariantCulture), out var element)
                    && element.ValueKind == JsonValueKind.Object
                    && element.Deserialize<TmdbTvSeason>(Json) is { } season)
                {
                    seasons.Add(season);
                }
            }
        }

        return seasons;
    }

    public async Task<TmdbTvFindResponse?> FindByTvdbIdAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var url = $"find/{tvdbId}?external_source=tvdb_id&language={Uri.EscapeDataString(options.Value.Language)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbTvFindResponse>(Json, cancellationToken);
    }

    public async Task<TmdbTvExternalIds?> GetTvExternalIdsAsync(int tmdbId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"tv/{tmdbId}/external_ids", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbTvExternalIds>(Json, cancellationToken);
    }

    public async Task<TmdbTvSearchResponse?> SearchTvAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"search/tv?query={Uri.EscapeDataString(query)}&include_adult=false&language={Uri.EscapeDataString(options.Value.Language)}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TmdbTvSearchResponse>(Json, cancellationToken);
    }
}
