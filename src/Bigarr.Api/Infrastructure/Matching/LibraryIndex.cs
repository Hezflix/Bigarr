using Bigarr.Domain.Movies;
using Bigarr.Parsing;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>The library keyed the way releases identify films: TMDB id, IMDb id, cleaned titles (own, original, alternative).</summary>
public sealed class LibraryIndex
{
    private readonly Dictionary<int, Movie> _byTmdb = [];
    private readonly Dictionary<string, Movie> _byImdb = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Movie>> _byTitle = new(StringComparer.Ordinal);

    private LibraryIndex(IReadOnlyCollection<Movie> movies)
    {
        Movies = movies;
        foreach (var movie in movies)
        {
            _byTmdb[movie.TmdbId] = movie;
            if (!string.IsNullOrWhiteSpace(movie.ImdbId))
            {
                _byImdb[movie.ImdbId] = movie;
            }

            foreach (var title in new[] { movie.Title, movie.OriginalTitle }.Concat(movie.AlternativeTitles.Select(a => a.Title)))
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var clean = title.CleanMovieTitle();
                if (clean.Length == 0)
                {
                    continue;
                }

                if (!_byTitle.TryGetValue(clean, out var list))
                {
                    _byTitle[clean] = list = [];
                }

                if (!list.Contains(movie))
                {
                    list.Add(movie);
                }
            }
        }
    }

    public IReadOnlyCollection<Movie> Movies { get; }

    public static LibraryIndex Build(IReadOnlyCollection<Movie> movies) => new(movies);

    public Movie? ByTmdb(int tmdbId) => _byTmdb.GetValueOrDefault(tmdbId);

    public Movie? ByImdb(string imdbId) => _byImdb.GetValueOrDefault(imdbId);

    public IReadOnlyList<Movie> ByCleanTitle(string cleanTitle) => _byTitle.TryGetValue(cleanTitle, out var list) ? list : [];
}
