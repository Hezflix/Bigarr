using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Bigarr.Parsing;
using Bigarr.Parsing.Model;

namespace Bigarr.Api.Features.Search;

/// <summary>Is this release the film we searched for? Identifiers first, then titles and year (blueprint §5).</summary>
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
