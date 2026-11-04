using Bigarr.Api.Infrastructure.Auth;
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

        return app;
    }
}
