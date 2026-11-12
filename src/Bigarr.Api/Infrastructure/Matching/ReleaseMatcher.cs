using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Bigarr.Parsing.Model;

namespace Bigarr.Api.Infrastructure.Matching;

/// <summary>Is this release the film we searched for? Identifiers first, then titles and year.</summary>
public static class ReleaseMatcher
{
    public static ParsedMovieInfo? TryParse(string title)
    {
        try
        {
            return Parser.ParseMovieTitle(title);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? Match(ParsedMovieInfo? parsed, ReleaseInfo release, Movie movie)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(movie);

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.PrimaryMovieTitle))
        {
            return "Release title could not be parsed";
        }

        // Identifiers, from the title or from the indexer, settle it either way.
        var tmdbId = parsed.TmdbId > 0 ? parsed.TmdbId : release.TmdbId;
        if (tmdbId is > 0)
        {
            return tmdbId == movie.TmdbId ? null : $"Release is TMDB {tmdbId}, not {movie.TmdbId}";
        }

        var imdbId = Normalize(parsed.ImdbId) ?? Normalize(release.ImdbId);
        var movieImdb = Normalize(movie.ImdbId);
        if (imdbId is not null && movieImdb is not null)
        {
            return string.Equals(imdbId, movieImdb, StringComparison.OrdinalIgnoreCase) ? null : $"Release is IMDb {imdbId}, not {movieImdb}";
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in new[] { movie.Title, movie.OriginalTitle }.Concat(movie.AlternativeTitles.Select(a => a.Title)))
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                var clean = title.CleanMovieTitle();
                if (clean.Length > 0)
                {
                    wanted.Add(clean);
                }
            }
        }

        var parsedTitles = parsed.MovieTitles.Select(t => t.CleanMovieTitle()).Where(t => t.Length > 0).ToList();
        if (!parsedTitles.Any(wanted.Contains))
        {
            return $"Title '{parsed.PrimaryMovieTitle}' does not match {movie.Title}";
        }

        if (parsed.Year > 0 && movie.Year is not null && Math.Abs(parsed.Year - movie.Year.Value) > 1)
        {
            return $"Year {parsed.Year} does not match {movie.Year}";
        }

        return null;
    }

    /// <summary>Which film of the library, if any, a release belongs to. Identifiers settle it; titles need a year to disambiguate.</summary>
    public static (Movie? Movie, string? Reason) FindMovie(ParsedMovieInfo? parsed, ReleaseInfo release, LibraryIndex library)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(library);

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.PrimaryMovieTitle))
        {
            return (null, "Release title could not be parsed");
        }

        var tmdbId = parsed.TmdbId > 0 ? parsed.TmdbId : release.TmdbId;
        if (tmdbId is > 0)
        {
            var byTmdb = library.ByTmdb(tmdbId.Value);
            return byTmdb is null ? (null, $"TMDB {tmdbId} is not in the library") : (byTmdb, null);
        }

        var imdbId = Normalize(parsed.ImdbId) ?? Normalize(release.ImdbId);
        if (imdbId is not null)
        {
            var byImdb = library.ByImdb(imdbId);
            if (byImdb is not null)
            {
                return (byImdb, null);
            }

            // An IMDb id we do not know: the title may still tell, some indexers attach wrong ids.
        }

        var candidates = parsed.MovieTitles
            .Select(t => t.CleanMovieTitle())
            .Where(t => t.Length > 0)
            .SelectMany(library.ByCleanTitle)
            .Distinct()
            .ToList();

        if (candidates.Count == 0)
        {
            return (null, imdbId is null
                ? $"No movie in the library matches '{parsed.PrimaryMovieTitle}'"
                : $"IMDb {imdbId} is not in the library and no title matches '{parsed.PrimaryMovieTitle}'");
        }

        if (parsed.Year > 0)
        {
            var sameYear = candidates.Where(m => m.Year is null || Math.Abs(m.Year.Value - parsed.Year) <= 1).ToList();
            if (sameYear.Count == 0)
            {
                return (null, $"'{parsed.PrimaryMovieTitle}' is in the library but not from {parsed.Year}");
            }

            candidates = sameYear;
        }

        return candidates.Count == 1
            ? (candidates[0], null)
            : (null, $"Several movies in the library match '{parsed.PrimaryMovieTitle}'{(parsed.Year > 0 ? $" ({parsed.Year})" : " and the release has no year")}");
    }

    private static string? Normalize(string? imdbId)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return null;
        }

        var digits = imdbId.Trim().TrimStart('t', 'T');
        return digits.Length == 0 || !digits.All(char.IsAsciiDigit) ? null : "tt" + digits.PadLeft(7, '0');
    }
}
