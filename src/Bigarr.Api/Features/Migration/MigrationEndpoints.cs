using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Migration;

/// <summary>Moving in from an existing Radarr (M8): preview the mapping, run the import in the background, follow it.</summary>
public static class MigrationEndpoints
{
    public static IEndpointRouteBuilder MapMigration(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/migration/radarr").WithTags("Migration").RequireAuthorization(AuthPolicies.Api);

        group.MapPost("/preview", async Task<Results<Ok<RadarrImportPreviewResponse>, ProblemHttpResult>> (RadarrImportPreviewRequest request, RadarrImportService imports, CancellationToken ct) =>
        {
            var result = await imports.PreviewAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/import", async Task<Results<Accepted<RadarrImportStatusResponse>, ProblemHttpResult>> (RadarrImportRequest request, RadarrImportService imports, RadarrImportRunner runner, CancellationToken ct) =>
        {
            if (await imports.ValidateAsync(request, ct) is { } invalid)
            {
                return invalid.ToProblem();
            }

            var status = runner.Enqueue(request);
            return status is null
                ? Infrastructure.Failure.Conflict("An import is already running", "Wait for it to finish before starting another one.").ToProblem()
                : TypedResults.Accepted("/api/v1/migration/radarr/status", status);
        });

        group.MapGet("/status", Ok<RadarrImportStatusResponse> (RadarrImportRunner runner) => TypedResults.Ok(runner.Status()));

        app.MapTautulliImport();
        return app;
    }

    /// <summary>Moving in from Tautulli: a copy of its database, typed as a path or uploaded, previewed, then imported in the background.</summary>
    private static void MapTautulliImport(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/migration/tautulli").WithTags("Migration").RequireAuthorization(AuthPolicies.Api);

        // The raw file as the body: a Tautulli database runs to hundreds of megabytes, past any form limit.
        group.MapPut("/upload", async Task<Results<Ok<TautulliUploadResponse>, ProblemHttpResult>> (HttpRequest http, TautulliImportService imports, TautulliImportRunner runner, CancellationToken ct) =>
        {
            if (runner.IsBusy)
            {
                return Infrastructure.Failure.Conflict("An import is running", "Wait for it to finish before uploading another database.").ToProblem();
            }

            if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = null;
            }

            return TypedResults.Ok(await imports.SaveUploadAsync(http.Body, ct));
        });

        group.MapPost("/preview", async Task<Results<Ok<TautulliImportPreviewResponse>, ProblemHttpResult>> (TautulliImportPreviewRequest request, TautulliImportService imports, CancellationToken ct) =>
        {
            var result = await imports.PreviewAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/import", async Task<Results<Accepted<TautulliImportStatusResponse>, ProblemHttpResult>> (TautulliImportRequest request, TautulliImportService imports, TautulliImportRunner runner, CancellationToken ct) =>
        {
            if (await imports.ValidateAsync(request, ct) is { } invalid)
            {
                return invalid.ToProblem();
            }

            var status = runner.Enqueue(request);
            return status is null
                ? Infrastructure.Failure.Conflict("An import is already running", "Wait for it to finish before starting another one.").ToProblem()
                : TypedResults.Accepted("/api/v1/migration/tautulli/status", status);
        });

        group.MapGet("/status", Ok<TautulliImportStatusResponse> (TautulliImportRunner runner) => TypedResults.Ok(runner.Status()));
    }
}
