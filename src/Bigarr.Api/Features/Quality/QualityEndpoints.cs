using System.Text.Json;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Quality;

public static class QualityEndpoints
{
    public static IEndpointRouteBuilder MapQuality(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Quality").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/qualityprofiles", async Task<Ok<List<QualityProfileSummary>>> (QualityProfileService profiles, CancellationToken ct) =>
            TypedResults.Ok(await profiles.ListAsync(ct)));

        group.MapGet("/qualityprofiles/schema", async Task<Ok<QualityProfileResponse>> (QualityProfileService profiles, CancellationToken ct) =>
            TypedResults.Ok(await profiles.SchemaAsync(ct)));

        group.MapGet("/qualityprofiles/{id:int}", async Task<Results<Ok<QualityProfileResponse>, NotFound>> (int id, QualityProfileService profiles, CancellationToken ct) =>
        {
            var profile = await profiles.GetAsync(id, ct);
            return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
        });

        group.MapPost("/qualityprofiles", async Task<Results<Created<QualityProfileResponse>, ProblemHttpResult>> (QualityProfileRequest request, QualityProfileService profiles, CancellationToken ct) =>
        {
            var result = await profiles.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/qualityprofiles/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/qualityprofiles/{id:int}", async Task<Results<Ok<QualityProfileResponse>, ProblemHttpResult>> (int id, QualityProfileRequest request, QualityProfileService profiles, CancellationToken ct) =>
        {
            var result = await profiles.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/qualityprofiles/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, QualityProfileService profiles, CancellationToken ct) =>
        {
            var result = await profiles.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        group.MapGet("/qualitydefinitions", async Task<Ok<List<QualityDefinitionResponse>>> (QualityProfileService profiles, CancellationToken ct) =>
            TypedResults.Ok(await profiles.ListDefinitionsAsync(ct)));

        group.MapPut("/qualitydefinitions/{id:int}", async Task<Results<Ok<QualityDefinitionResponse>, ProblemHttpResult>> (int id, QualityDefinitionUpdateRequest request, QualityProfileService profiles, CancellationToken ct) =>
        {
            var result = await profiles.UpdateDefinitionAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapGet("/customformats", async Task<Ok<List<CustomFormatResponse>>> (CustomFormatService formats, CancellationToken ct) =>
            TypedResults.Ok(await formats.ListAsync(ct)));

        // The condition types and their fields, so the editor builds formats by hand without knowing Radarr's names.
        group.MapGet("/customformats/schema", Ok<CustomFormatSchemaResponse> () => TypedResults.Ok(CustomFormatService.Schema()));

        // A draft judged against a release title before it is saved.
        group.MapPost("/customformats/test", Results<Ok<CustomFormatTestResponse>, ProblemHttpResult> (CustomFormatTestRequest request, CustomFormatService formats) =>
        {
            var result = formats.Test(request);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Paste-and-go: the body is the Radarr export or guide JSON itself, one format or an array.
        group.MapPost("/customformats/import", async Task<Results<Ok<CustomFormatImportResponse>, ProblemHttpResult>> (
            JsonElement body, CustomFormatService formats, CancellationToken ct, string? profileIds = null, string scoreKey = "default") =>
        {
            var ids = new List<int>();
            foreach (var part in (profileIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, out var id))
                {
                    return TypedResults.Problem(title: "Invalid profileIds", detail: "Comma-separated quality profile ids.", statusCode: StatusCodes.Status400BadRequest);
                }

                ids.Add(id);
            }

            var result = await formats.ImportAsync(body.GetRawText(), ids, string.IsNullOrWhiteSpace(scoreKey) ? "default" : scoreKey, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapGet("/customformats/evaluate", async Task<Results<Ok<CustomFormatEvaluationResponse>, ValidationProblem, ProblemHttpResult>> (
            string? title, CustomFormatService formats, BigarrDbContext db, CancellationToken ct, long? size = null, int? movieId = null, string? originalLanguage = null, IndexerFlags flags = IndexerFlags.None) =>
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["A release title is required."] });
            }

            // A release naming no language speaks the film's original language; without a film that language is unknown.
            int? year = null;
            if (movieId is not null)
            {
                var movie = await db.Movies.AsNoTracking().Where(m => m.Id == movieId).Select(m => new { m.Year, m.OriginalLanguage }).FirstOrDefaultAsync(ct);
                if (movie is null)
                {
                    return TypedResults.Problem(title: "Movie not found", statusCode: StatusCodes.Status404NotFound);
                }

                year = movie.Year;
                originalLanguage ??= movie.OriginalLanguage;
            }

            var result = await formats.EvaluateAsync(title.Trim(), size, flags, year, originalLanguage, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapGet("/customformats/{id:int}", async Task<Results<Ok<CustomFormatResponse>, NotFound>> (int id, CustomFormatService formats, CancellationToken ct) =>
        {
            var format = await formats.GetAsync(id, ct);
            return format is null ? TypedResults.NotFound() : TypedResults.Ok(format);
        });

        group.MapPost("/customformats", async Task<Results<Created<CustomFormatResponse>, ProblemHttpResult>> (CustomFormatRequest request, CustomFormatService formats, CancellationToken ct) =>
        {
            var result = await formats.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/customformats/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/customformats/{id:int}", async Task<Results<Ok<CustomFormatResponse>, ProblemHttpResult>> (int id, CustomFormatRequest request, CustomFormatService formats, CancellationToken ct) =>
        {
            var result = await formats.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/customformats/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, CustomFormatService formats, CancellationToken ct) =>
        {
            var result = await formats.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        return app;
    }
}
