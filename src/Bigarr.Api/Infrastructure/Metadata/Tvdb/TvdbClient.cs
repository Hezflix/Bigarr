using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Metadata.Tvdb;

/// <summary>Thin typed client over TVDB API v4. Authentication and retries live in <see cref="TvdbAuthHandler"/>.</summary>
public sealed class TvdbClient(HttpClient http, IOptions<TvdbOptions> options, ILogger<TvdbClient> logger)
{
    /// <summary>TVDB serves 500 episodes per page.</summary>
    public const int EpisodePageSize = 500;

    /// <summary>A guard against a provider that keeps answering full pages: 100 pages = 50,000 episodes.</summary>
    private const int MaxPages = 100;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public async Task<IReadOnlyList<TvdbSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var url = $"search?query={Uri.EscapeDataString(query)}&type=series&limit=20";
        using var response = await http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<TvdbEnvelope<List<TvdbSearchHit>>>(Json, cancellationToken);
        return envelope?.Data ?? [];
    }

    public async Task<TvdbSeriesExtended?> GetSeriesAsync(int tvdbId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"series/{tvdbId}/extended?meta=translations&short=true", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<TvdbEnvelope<TvdbSeriesExtended>>(Json, cancellationToken);
        return envelope?.Data;
    }

    /// <summary>A TVDB film (the record a special flagged as a movie links to), for its TMDB and IMDb ids.</summary>
    public async Task<TvdbMovieExtended?> GetMovieAsync(int tvdbMovieId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"movies/{tvdbMovieId}/extended?short=true", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<TvdbEnvelope<TvdbMovieExtended>>(Json, cancellationToken);
        return envelope?.Data;
    }

    /// <summary>
    /// Every episode of the default (aired) order, titles and overviews in the configured language. Pages are counted
    /// here: <c>links.next</c> is a URL, so the client asks for page+1 until it holds <c>total_items</c> episodes or a
    /// page comes back short.
    /// </summary>
    public async Task<IReadOnlyList<TvdbEpisode>> GetEpisodesAsync(int tvdbId, CancellationToken cancellationToken)
    {
        var language = Uri.EscapeDataString(options.Value.Language);
        var episodes = new List<TvdbEpisode>();
        for (var page = 0; page < MaxPages; page++)
        {
            using var response = await http.GetAsync($"series/{tvdbId}/episodes/default/{language}?page={page}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                break;
            }

            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<TvdbEnvelope<TvdbEpisodePage>>(Json, cancellationToken);
            var batch = envelope?.Data?.Episodes ?? [];
            episodes.AddRange(batch);

            var total = envelope?.Links?.TotalItems;
            var pageSize = envelope?.Links?.PageSize is > 0 and var size ? size : EpisodePageSize;
            if (batch.Count == 0 || batch.Count < pageSize || (total is not null && episodes.Count >= total))
            {
                return episodes;
            }
        }

        logger.LogWarning("TVDB series {TvdbId}: stopped after {Pages} pages of episodes", tvdbId, MaxPages);
        return episodes;
    }
}
