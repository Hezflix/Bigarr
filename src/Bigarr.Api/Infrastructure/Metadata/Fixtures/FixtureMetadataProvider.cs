using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;

namespace Bigarr.Api.Infrastructure.Metadata.Fixtures;

/// <summary>
/// A dozen well-known films embedded in the assembly. Used by the tests and whenever no TMDB key is configured,
/// so development never depends on the network.
/// </summary>
public sealed class FixtureMetadataProvider : IMetadataProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyList<MovieMetadata> _movies;

    public FixtureMetadataProvider()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Fixtures.movies.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        _movies = JsonSerializer.Deserialize<List<MovieMetadata>>(stream, Json) ?? [];
    }

    public string Name => "fixtures";

    public IReadOnlyList<MovieMetadata> All => _movies;

    public Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
        Task.FromResult(_movies.FirstOrDefault(m => m.TmdbId == tmdbId));

    public Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken) =>
        Task.FromResult(_movies.FirstOrDefault(m => string.Equals(m.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken)
    {
        var needle = query.CleanMovieTitle();
        if (needle.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<MovieMetadata>>([]);
        }

        var results = _movies
            .Where(m => year is null or 0 || m.Year == year || m.Year == year - 1 || m.Year == year + 1)
            .Select(m => (Movie: m, Score: Score(m, needle)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Movie.Title)
            .Select(x => x.Movie)
            .ToList();

        return Task.FromResult<IReadOnlyList<MovieMetadata>>(results);
    }

    private static int Score(MovieMetadata movie, string needle)
    {
        var titles = new List<string> { movie.Title };
        if (movie.OriginalTitle is not null)
        {
            titles.Add(movie.OriginalTitle);
        }

        titles.AddRange(movie.AlternativeTitles.Select(a => a.Title));

        var best = 0;
        foreach (var title in titles.Select(t => t.CleanMovieTitle()).Where(t => t.Length > 0))
        {
            if (title == needle)
            {
                return 3;
            }

            if (title.StartsWith(needle, StringComparison.Ordinal))
            {
                best = Math.Max(best, 2);
            }
            else if (title.Contains(needle, StringComparison.Ordinal))
            {
                best = Math.Max(best, 1);
            }
        }

        return best;
    }
}
