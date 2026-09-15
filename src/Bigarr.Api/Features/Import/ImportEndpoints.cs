using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Import;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImport(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/v1/mediamanagement").WithTags("Media management").RequireAuthorization(AuthPolicies.Api);

        settings.MapGet("/", async Task<Ok<MediaManagementResponse>> (MediaManagementService service, CancellationToken ct) =>
            TypedResults.Ok(service.ToResponse(await service.GetAsync(ct))));

        settings.MapPut("/", async Task<Results<Ok<MediaManagementResponse>, ProblemHttpResult>> (MediaManagementRequest request, MediaManagementService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(service.ToResponse(result.Value!)) : result.Failure!.ToProblem();
        });

        // A draft template rendered on the sample film, with the errors that would refuse it: what the settings form shows while typing.
        settings.MapPost("/naming/preview", Ok<NamingPreviewResponse> (NamingPreviewRequest request) => TypedResults.Ok(MediaManagementService.Preview(request)));

        settings.MapPost("/backfill", async Task<Ok<BackfillResponse>> (int? batch, MediaMaintenance maintenance, CancellationToken ct) =>
            TypedResults.Ok(await maintenance.BackfillAsync(batch, ct)));

        var manual = app.MapGroup("/api/v1/manualimport").WithTags("Manual import").RequireAuthorization(AuthPolicies.Api);

        manual.MapGet("/", async Task<Results<Ok<ManualImportResponse>, ProblemHttpResult>> (string? path, int? grabId, int? movieId, ManualImportService service, CancellationToken ct) =>
        {
            var result = await service.ListAsync(path, grabId, movieId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        manual.MapPost("/", async Task<Results<Ok<List<ManualImportResultResponse>>, ProblemHttpResult>> (ManualImportRequest request, ManualImportService service, CancellationToken ct) =>
        {
            var result = await service.ImportAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // A failed import goes back to pending and the worker tries again.
        app.MapPost("/api/v1/queue/{grabId:int}/import", async Task<Results<NoContent, ProblemHttpResult>> (int grabId, ImportService imports, CancellationToken ct) =>
        {
            var result = await imports.RetryAsync(grabId, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        }).WithTags("Queue").RequireAuthorization(AuthPolicies.Api);

        var rename = app.MapGroup("/api/v1/rename").WithTags("Media management").RequireAuthorization(AuthPolicies.Api);

        rename.MapGet("/", async Task<Results<Ok<List<RenamePreviewResponse>>, ProblemHttpResult>> (int movieId, RenameService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(movieId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        rename.MapPost("/", async Task<Results<Ok<List<RenameResultResponse>>, ProblemHttpResult>> (RenameRequest request, RenameService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
