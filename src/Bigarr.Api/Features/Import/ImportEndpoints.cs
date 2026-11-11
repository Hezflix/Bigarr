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

        settings.MapGet("/series-naming", async Task<Ok<SeriesNamingResponse>> (MediaManagementService service, CancellationToken ct) =>
            TypedResults.Ok(service.ToSeriesNaming(await service.GetAsync(ct))));

        settings.MapPut("/series-naming", async Task<Results<Ok<SeriesNamingResponse>, ProblemHttpResult>> (SeriesNamingRequest request, MediaManagementService service, CancellationToken ct) =>
        {
            var result = await service.UpdateSeriesNamingAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(service.ToSeriesNaming(result.Value!)) : result.Failure!.ToProblem();
        });

        // Draft series templates rendered on Sonarr's sample series, with the errors that would refuse each.
        settings.MapPost("/series-naming/preview", Ok<SeriesNamingPreviewResponse> (SeriesNamingPreviewRequest request) => TypedResults.Ok(MediaManagementService.PreviewSeries(request)));

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

        manual.MapGet("/series", async Task<Results<Ok<SeriesManualImportResponse>, ProblemHttpResult>> (string? path, int? grabId, int? seriesId, SeriesManualImportService service, CancellationToken ct) =>
        {
            var result = await service.ListAsync(path, grabId, seriesId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        manual.MapPost("/series", async Task<Results<Ok<List<SeriesManualImportResultResponse>>, ProblemHttpResult>> (SeriesManualImportRequest request, SeriesManualImportService service, CancellationToken ct) =>
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

        // Series: rename to the current templates (per series, version or season), files of a series, the scan's unmatched files.
        var series = app.MapGroup("/api/v1/series/{seriesId:int}").WithTags("Series files").RequireAuthorization(AuthPolicies.Api);

        series.MapGet("/rename", async Task<Results<Ok<List<SeriesRenamePreviewResponse>>, ProblemHttpResult>> (int seriesId, int? versionId, int? seasonNumber, SeriesRenameService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(seriesId, versionId, seasonNumber, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        series.MapPost("/rename", async Task<Results<Ok<List<SeriesRenameResultResponse>>, ProblemHttpResult>> (int seriesId, SeriesRenameRequest request, SeriesRenameService service, CancellationToken ct) =>
        {
            var result = await service.RenameAsync(request with { SeriesId = seriesId }, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        series.MapGet("/episodefiles/{fileId:int}", async Task<Results<Ok<EpisodeFileDetailResponse>, ProblemHttpResult>> (int seriesId, int fileId, SeriesManualImportService service, CancellationToken ct) =>
        {
            var result = await service.EpisodeFileAsync(seriesId, fileId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        series.MapDelete("/episodefiles/{fileId:int}", async Task<Results<NoContent, ProblemHttpResult>> (int seriesId, int fileId, SeriesManualImportService service, CancellationToken ct) =>
        {
            var result = await service.DeleteEpisodeFileAsync(seriesId, fileId, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        series.MapPost("/unmatched/{unmatchedId:int}/assign", async Task<Results<Ok<EpisodeFileDetailResponse>, ProblemHttpResult>> (int seriesId, int unmatchedId, AssignUnmatchedRequest request, SeriesManualImportService service, CancellationToken ct) =>
        {
            var result = await service.AssignUnmatchedAsync(seriesId, unmatchedId, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
