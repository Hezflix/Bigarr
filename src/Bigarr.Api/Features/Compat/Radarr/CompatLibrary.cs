using System.Linq.Expressions;
using Bigarr.Api.Features.Movies;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>One line of the compat library: a version and its film. The line's <c>id</c> is the version's (§7.1).</summary>
public sealed record CompatRow(Movie Movie, MovieVersion Version);

/// <summary>
/// The API key's view applied before mapping (blueprint §3.8, §7.1): <c>default</c> shows a film as its default version,
/// <c>all</c> one line per version, <c>version:&lt;name&gt;</c> only the versions of that name. An id outside the view
/// does not exist for the caller.
/// </summary>
public sealed class CompatLibrary(MovieService movies)
{
    public static bool Sees(CompatView view, MovieVersion version)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(version);

        return view.Kind switch
        {
            CompatViewKind.Default => version.IsDefault,
            CompatViewKind.All => true,
            _ => string.Equals(version.Name, view.VersionName, StringComparison.Ordinal),
        };
    }

    /// <summary>The version the view designates in a film, or null: the default one, the named one; <c>all</c> designates nothing.</summary>
    public static MovieVersion? Designated(CompatView view, Movie movie)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(movie);

        return view.Kind switch
        {
            CompatViewKind.Default => movie.Versions.FirstOrDefault(v => v.IsDefault),
            CompatViewKind.Version => movie.Versions.FirstOrDefault(v => v.Name == view.VersionName),
            _ => null,
        };
    }

    /// <summary>The versions the view shows, as a predicate the database evaluates.</summary>
    public static Expression<Func<MovieVersion, bool>> Predicate(CompatView view) => view.Kind switch
    {
        CompatViewKind.Default => v => v.IsDefault,
        CompatViewKind.All => v => true,
        _ => v => v.Name == view.VersionName,
    };

    /// <summary>Every line of the view, films sorted by title.</summary>
    public async Task<List<CompatRow>> RowsAsync(CompatView view, CancellationToken ct)
    {
        var predicate = Predicate(view);
        var films = await movies.WithGraph().Where(m => m.Versions.AsQueryable().Any(predicate)).OrderBy(m => m.SortTitle).ThenBy(m => m.Year).ToListAsync(ct);
        return films.SelectMany(m => m.Versions.Where(v => Sees(view, v)).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).Select(v => new CompatRow(m, v))).ToList();
    }

    /// <summary>The line behind a compat id, or null when the version is unknown or outside the view.</summary>
    public async Task<CompatRow?> FindAsync(CompatView view, int versionId, CancellationToken ct)
    {
        var movie = await movies.WithGraph().FirstOrDefaultAsync(m => m.Versions.Any(v => v.Id == versionId), ct);
        var version = movie?.Versions.FirstOrDefault(v => v.Id == versionId);
        return movie is null || version is null || !Sees(view, version) ? null : new CompatRow(movie, version);
    }

    public Task<Movie?> FindMovieByTmdbAsync(int tmdbId, CancellationToken ct) =>
        movies.WithGraph().FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);

    public Task<Movie?> FindMovieAsync(int movieId, CancellationToken ct) =>
        movies.WithGraph().FirstOrDefaultAsync(m => m.Id == movieId, ct);

    /// <summary>The version ids the view shows, for filtering the queue and the history without loading films.</summary>
    public async Task<HashSet<int>> VisibleVersionIdsAsync(CompatView view, CancellationToken ct)
    {
        var predicate = Predicate(view);
        var ids = await movies.WithGraph().SelectMany(m => m.Versions.AsQueryable().Where(predicate).Select(v => v.Id)).ToListAsync(ct);
        return ids.ToHashSet();
    }
}
