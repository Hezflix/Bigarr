using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

public static class MoviesEndpoints
{
    public static IEndpointRouteBuilder MapMovies(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/movies").WithTags("Movies").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", ListAsync);
        group.MapGet("/lookup", LookupAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("/", AddAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/{id:int}/refresh", RefreshAsync);
        group.MapPost("/{id:int}/versions", AddVersionAsync);
        group.MapPut("/{id:int}/versions/{versionId:int}", UpdateVersionAsync);
        group.MapDelete("/{id:int}/versions/{versionId:int}", DeleteVersionAsync);
        group.MapPost("/{id:int}/versions/{versionId:int}/promote", PromoteAsync);
        group.MapDelete("/{id:int}/versions/{versionId:int}/file", DeleteFileAsync);

        return app;
    }

    private static async Task<Ok<List<MovieSummary>>> ListAsync(MovieService movies, CancellationToken ct)
    {
        var all = await movies.WithGraph().OrderBy(m => m.SortTitle).ThenBy(m => m.Year).ToListAsync(ct);
        var active = await movies.ActiveTargetsAsync(all.SelectMany(m => m.Versions).Select(v => v.Id), ct);
        var today = movies.Today;
        return TypedResults.Ok(all.Select(m => MovieMapper.ToSummary(m, active, today)).ToList());
    }

    private static async Task<Results<Ok<List<MovieLookupResult>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        string? term, MovieService movies, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["term"] = ["A search term is required."] });
        }

        var result = await movies.LookupAsync(term, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value!.ToList()) : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<MovieResponse>, NotFound>> GetAsync(int id, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var movie = await movies.WithGraph().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (movie is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ToResponseAsync(movie, movies, scorer, ct));
    }

    private static async Task<Results<Created<MovieResponse>, ProblemHttpResult>> AddAsync(AddMovieRequest request, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.AddMovieAsync(request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var movie = await movies.WithGraph().FirstAsync(m => m.Id == result.Value!.Id, ct);
        return TypedResults.Created($"/api/v1/movies/{movie.Id}", await ToResponseAsync(movie, movies, scorer, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        int id, MovieService movies, CancellationToken ct, bool deleteFiles = false, bool addImportExclusion = false)
    {
        var result = await movies.DeleteMovieAsync(id, deleteFiles, addImportExclusion, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<MovieResponse>, ProblemHttpResult>> RefreshAsync(int id, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.RefreshAsync(id, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var movie = await movies.WithGraph().FirstAsync(m => m.Id == id, ct);
        return TypedResults.Ok(await ToResponseAsync(movie, movies, scorer, ct));
    }

    private static async Task<Results<Created<VersionResponse>, ProblemHttpResult>> AddVersionAsync(
        int id, AddVersionRequest request, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.AddVersionAsync(id, request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var version = await LoadVersionAsync(id, result.Value!.Id, movies, scorer, ct);
        return TypedResults.Created($"/api/v1/movies/{id}/versions/{version.Id}", version);
    }

    private static async Task<Results<Ok<VersionResponse>, ProblemHttpResult>> UpdateVersionAsync(
        int id, int versionId, UpdateVersionRequest request, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.UpdateVersionAsync(id, versionId, request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        return TypedResults.Ok(await LoadVersionAsync(id, versionId, movies, scorer, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteVersionAsync(
        int id, int versionId, MovieService movies, CancellationToken ct, bool deleteFiles = false, int? promote = null, bool addImportExclusion = false)
    {
        var result = await movies.DeleteVersionAsync(id, versionId, deleteFiles, promote, addImportExclusion, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<VersionResponse>, ProblemHttpResult>> PromoteAsync(
        int id, int versionId, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.PromoteAsync(id, versionId, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        return TypedResults.Ok(await LoadVersionAsync(id, versionId, movies, scorer, ct));
    }

    private static async Task<Results<Ok<VersionResponse>, ProblemHttpResult>> DeleteFileAsync(
        int id, int versionId, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = await movies.DeleteFileAsync(id, versionId, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        return TypedResults.Ok(await LoadVersionAsync(id, versionId, movies, scorer, ct));
    }

    private static async Task<MovieResponse> ToResponseAsync(Movie movie, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var active = await movies.ActiveTargetsAsync(movie.Versions.Select(v => v.Id), ct);
        return MovieMapper.ToResponse(movie, active, movies.Today, await AssessFilesAsync(movie, scorer, ct));
    }

    private static async Task<VersionResponse> LoadVersionAsync(int movieId, int versionId, MovieService movies, CustomFormatScorer scorer, CancellationToken ct)
    {
        var movie = await movies.WithGraph().FirstAsync(m => m.Id == movieId, ct);
        var version = movie.Versions.First(v => v.Id == versionId);
        var active = await movies.ActiveTargetsAsync([versionId], ct);
        var assessments = await AssessFilesAsync(movie, scorer, ct);
        return MovieMapper.ToVersion(version, active.Contains(versionId), movies.Today, assessments.GetValueOrDefault(versionId));
    }

    /// <summary>Custom formats and cutoff of every file of the film, judged now against the version's profile (§3.6).</summary>
    private static async Task<Dictionary<int, FileAssessment>> AssessFilesAsync(Movie movie, CustomFormatScorer scorer, CancellationToken ct)
    {
        var result = new Dictionary<int, FileAssessment>();
        var withFiles = movie.Versions.Where(v => v.File is not null).ToList();
        if (withFiles.Count == 0)
        {
            return result;
        }

        var calculator = await scorer.CalculatorAsync(ct);
        foreach (var version in withFiles)
        {
            var file = version.File!;
            var matched = calculator.Match(CustomFormatScorer.FileInput(
                file.OriginalReleaseTitle, file.RelativePath, file.Size, file.IndexerFlags, file.Quality, file.Languages, file.Edition, file.ReleaseGroup, movie.Year, movie.OriginalLanguage));
            result[version.Id] = CustomFormatScorer.Assess(version.QualityProfile, file.QualityId, matched);
        }

        return result;
    }
}
