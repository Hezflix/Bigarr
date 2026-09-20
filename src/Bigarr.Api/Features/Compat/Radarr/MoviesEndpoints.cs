using System.Security.Claims;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/movie</c> (§7.2, §7.3): one line per version the key sees, the line's id being the version's. Seerr's add flow
/// (lookup → PUT when unmonitored, POST when absent) creates the version the key designates, so two Radarr entries in
/// one Seerr, one per key, follow two versions of the same film without noticing each other.
/// </summary>
public static class MoviesEndpoints
{
    public static void MapCompatMovies(this RouteGroupBuilder group)
    {
        group.MapGet("/movie", ListAsync);
        group.MapGet("/movie/lookup", LookupAsync);
        group.MapGet("/movie/{id:int}", GetAsync);
        group.MapPost("/movie", AddAsync);
        group.MapPut("/movie", (RadarrMovieRequest request, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct) =>
            UpdateAsync(request.Id, request, user, library, movies, scorer, ct));
        group.MapPut("/movie/{id:int}", (int id, RadarrMovieRequest request, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct) =>
            UpdateAsync(id, request, user, library, movies, scorer, ct));
        group.MapDelete("/movie/{id:int}", DeleteAsync);
    }

    private static async Task<JsonHttpResult<List<RadarrMovie>>> ListAsync(ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct, int? tmdbId = null)
    {
        var view = user.CompatView();
        var rows = await library.RowsAsync(view, ct);
        if (tmdbId is not null)
        {
            rows = rows.Where(r => r.Movie.TmdbId == tmdbId).ToList();
        }

        var assessments = await RadarrMapper.AssessAsync(rows, scorer, ct);
        var today = movies.Today;
        return RadarrCompat.Ok(rows.Select(r => RadarrMapper.ToMovie(r, today, assessments.GetValueOrDefault(r.Version.Id))).ToList());
    }

    private static async Task<Results<JsonHttpResult<RadarrMovie>, NotFound>> GetAsync(int id, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var row = await library.FindAsync(user.CompatView(), id, ct);
        return row is null ? TypedResults.NotFound() : RadarrCompat.Ok(await ToMovieAsync(row, movies, scorer, ct));
    }

    /// <summary>
    /// Seerr's existence test (§7.2): the result carries an <c>id</c> only when the key's view sees a version of the film.
    /// A 4K key looking at a film that only has a 1080p gets <c>id: 0</c>, and POSTs.
    /// </summary>
    private static async Task<Results<JsonHttpResult<List<RadarrMovie>>, JsonHttpResult<RadarrError>>> LookupAsync(
        string? term, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return RadarrCompat.Error(StatusCodes.Status400BadRequest, "A search term is required.");
        }

        var result = await movies.LookupAsync(term, ct);
        if (!result.IsSuccess)
        {
            return RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
        }

        var view = user.CompatView();
        var today = movies.Today;
        var resources = new List<RadarrMovie>();
        foreach (var found in result.Value!)
        {
            CompatRow? row = null;
            if (found.ExistingMovieId is { } movieId)
            {
                var movie = await library.FindMovieAsync(movieId, ct);
                var version = movie is null ? null : view.Kind == CompatViewKind.All
                    ? movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).FirstOrDefault()
                    : CompatLibrary.Designated(view, movie);
                row = movie is null || version is null ? null : new CompatRow(movie, version);
            }

            resources.Add(row is null ? RadarrMapper.ToLookup(found, today) : await ToMovieAsync(row, movies, scorer, ct));
        }

        return RadarrCompat.Ok(resources);
    }

    private static async Task<Results<JsonHttpResult<RadarrMovie>, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>>> AddAsync(
        RadarrMovieRequest request, ClaimsPrincipal user, CompatLibrary library, MovieService movies, RootFolderService roots, CustomFormatScorer scorer, CancellationToken ct)
    {
        var view = user.CompatView();
        if (request.TmdbId <= 0)
        {
            return RadarrCompat.BadRequest("TmdbId", "A TMDB id is required.");
        }

        var profileId = request.QualityProfileId > 0 ? request.QualityProfileId : request.ProfileId;
        if (profileId <= 0)
        {
            return RadarrCompat.BadRequest("QualityProfileId", "A quality profile is required.");
        }

        var root = await roots.FindByPathAsync(request.RootFolderPath, ct);
        if (root is null)
        {
            return RadarrCompat.BadRequest("RootFolderPath", $"Unknown root folder '{request.RootFolderPath}'. Add it in Bigarr first.");
        }

        if (!TryParseAvailability(request.MinimumAvailability, out var availability))
        {
            return RadarrCompat.BadRequest("MinimumAvailability", "Expected announced, inCinemas or released.");
        }

        var search = request.AddOptions?.SearchForMovie ?? false;
        var movie = await library.FindMovieByTmdbAsync(request.TmdbId, ct);
        int versionId;
        if (movie is null)
        {
            // A new film: its default version, named after the key's view or, for a default key, after the profile.
            var name = view.Kind == CompatViewKind.Version ? view.VersionName : null;
            var added = await movies.AddMovieAsync(new AddMovieRequest(request.TmdbId, profileId, root.Id, name, request.Monitored, availability, search, request.Tags), ct);
            if (!added.IsSuccess)
            {
                return Translate(added.Failure!);
            }

            versionId = added.Value!.Versions.Single().Id;
        }
        else
        {
            if (view.Kind == CompatViewKind.All)
            {
                return RadarrCompat.BadRequest("TmdbId", "This key sees every version and designates none. Add versions with a version:<name> key or from Bigarr.");
            }

            if (CompatLibrary.Designated(view, movie) is not null)
            {
                return RadarrCompat.BadRequest("TmdbId", "This movie has already been added");
            }

            var added = await movies.AddVersionAsync(movie.Id, new AddVersionRequest(view.VersionName!, VersionMode.Managed, profileId, root.Id, null, request.Monitored, availability, null, search, request.Tags), ct);
            if (!added.IsSuccess)
            {
                return Translate(added.Failure!);
            }

            versionId = added.Value!.Id;
        }

        var row = await library.FindAsync(view, versionId, ct);
        return row is null
            ? RadarrCompat.Error(StatusCodes.Status500InternalServerError, "The version was created but is not visible to this key.")
            : RadarrCompat.Created(await ToMovieAsync(row, movies, scorer, ct));
    }

    /// <summary>Seerr re-monitors an unmonitored version this way and expects <c>monitored: true</c> back (§7.2). Radarr answers 202.</summary>
    private static async Task<Results<JsonHttpResult<RadarrMovie>, JsonHttpResult<List<RadarrValidationFailure>>, NotFound>> UpdateAsync(
        int id, RadarrMovieRequest request, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var view = user.CompatView();
        var row = await library.FindAsync(view, id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        if (!TryParseAvailability(request.MinimumAvailability, out var availability))
        {
            return RadarrCompat.BadRequest("MinimumAvailability", "Expected announced, inCinemas or released.");
        }

        var profileId = request.QualityProfileId > 0 ? request.QualityProfileId : request.ProfileId;
        var update = new UpdateVersionRequest(
            QualityProfileId: profileId > 0 ? profileId : null,
            Monitored: request.Monitored,
            MinimumAvailability: request.MinimumAvailability is null ? null : availability,
            TagIds: request.Tags);
        var updated = await movies.UpdateVersionAsync(row.Movie.Id, id, update, ct);
        if (!updated.IsSuccess)
        {
            return RadarrCompat.BadRequest("Id", updated.Failure!.Detail ?? updated.Failure.Title);
        }

        var fresh = await library.FindAsync(view, id, ct);
        return TypedResults.Json(await ToMovieAsync(fresh!, movies, scorer, ct), RadarrCompat.Json, statusCode: StatusCodes.Status202Accepted);
    }

    /// <summary>Deletes the version; the last one takes the film with it; a default version hands over to the oldest managed sibling (§3.11).</summary>
    private static async Task<Results<Ok, NotFound, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>>> DeleteAsync(
        int id, ClaimsPrincipal user, CompatLibrary library, MovieService movies, CancellationToken ct, bool deleteFiles = false, bool addImportExclusion = false)
    {
        var row = await library.FindAsync(user.CompatView(), id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        int? promote = null;
        var siblings = row.Movie.Versions.Where(v => v.Id != id).ToList();
        if (row.Version.IsDefault && siblings.Count > 0)
        {
            promote = siblings.Where(v => v.Mode == VersionMode.Managed).OrderBy(v => v.Id).Select(v => (int?)v.Id).FirstOrDefault();
            if (promote is null)
            {
                return RadarrCompat.BadRequest("Id", "This is the film's default version and no other managed version can take over. Delete the film from Bigarr instead.");
            }
        }

        var result = await movies.DeleteVersionAsync(row.Movie.Id, id, deleteFiles, promote, addImportExclusion, ct);
        return result.IsSuccess ? TypedResults.Ok() : RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
    }

    private static async Task<RadarrMovie> ToMovieAsync(CompatRow row, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var assessments = await RadarrMapper.AssessAsync([row], scorer, ct);
        return RadarrMapper.ToMovie(row, movies.Today, assessments.GetValueOrDefault(row.Version.Id));
    }

    private static bool TryParseAvailability(string? value, out MinimumAvailability availability)
    {
        availability = MinimumAvailability.Released;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        // Radarr also knows "tba" and "preDB" from older versions; both mean "as soon as possible".
        if (value.Equals("tba", StringComparison.OrdinalIgnoreCase) || value.Equals("preDB", StringComparison.OrdinalIgnoreCase))
        {
            availability = MinimumAvailability.Announced;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out availability) && Enum.IsDefined(availability);
    }

    /// <summary>Slice failures in Radarr's terms: a 400 with a validation list, or a plain error for the rest.</summary>
    private static Results<JsonHttpResult<RadarrMovie>, JsonHttpResult<List<RadarrValidationFailure>>, JsonHttpResult<RadarrError>> Translate(Failure failure) => failure.StatusCode switch
    {
        StatusCodes.Status404NotFound => RadarrCompat.BadRequest("TmdbId", "Movie not found on TMDB."),
        StatusCodes.Status409Conflict => RadarrCompat.BadRequest("TmdbId", "This movie has already been added"),
        StatusCodes.Status400BadRequest => RadarrCompat.BadRequest(failure.Title.Contains("profile", StringComparison.OrdinalIgnoreCase) ? "QualityProfileId" : "TmdbId", failure.Detail ?? failure.Title),
        _ => RadarrCompat.Error(failure.StatusCode, failure.Detail ?? failure.Title),
    };
}
