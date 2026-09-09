using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Indexers;

public static class IndexersEndpoints
{
    public static IEndpointRouteBuilder MapIndexers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/indexers").WithTags("Indexers").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<IndexerResponse>>> (IndexerService indexers, CancellationToken ct) =>
            TypedResults.Ok(await indexers.ListAsync(ct)));

        group.MapGet("/{id:int}", async Task<Results<Ok<IndexerResponse>, NotFound>> (int id, IndexerService indexers, CancellationToken ct) =>
        {
            var indexer = await indexers.GetAsync(id, ct);
            return indexer is null ? TypedResults.NotFound() : TypedResults.Ok(indexer);
        });

        group.MapPost("/", async Task<Results<Created<IndexerResponse>, ProblemHttpResult>> (IndexerRequest request, IndexerService indexers, CancellationToken ct) =>
        {
            var result = await indexers.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/indexers/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/{id:int}", async Task<Results<Ok<IndexerResponse>, ProblemHttpResult>> (int id, IndexerRequest request, IndexerService indexers, CancellationToken ct) =>
        {
            var result = await indexers.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, IndexerService indexers, CancellationToken ct) =>
        {
            var result = await indexers.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // Read-only Prowlarr sync, driven by Bigarr:Prowlarr configuration.
        group.MapGet("/prowlarr", Ok<ProwlarrStatusResponse> (ProwlarrSyncService prowlarr) => TypedResults.Ok(prowlarr.Status()));

        group.MapPost("/prowlarr/sync", async Task<Results<Ok<ProwlarrSyncResult>, ProblemHttpResult>> (ProwlarrSyncService prowlarr, CancellationToken ct) =>
        {
            var result = await prowlarr.SyncAsync(ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Test an unsaved configuration (the form) or a saved indexer (the row).
        group.MapPost("/test", async Task<Results<Ok<IndexerTestResponse>, ProblemHttpResult>> (IndexerRequest request, IndexerService indexers, CancellationToken ct) =>
        {
            var result = await indexers.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/{id:int}/test", async Task<Results<Ok<IndexerTestResponse>, ProblemHttpResult>> (int id, IndexerService indexers, CancellationToken ct) =>
        {
            var result = await indexers.TestAsync(null, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
