using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Movies;

/// <summary>The library's bulk editor, under the movies group: one action on one version of many films.</summary>
public static class BulkEditEndpoints
{
    public static RouteGroupBuilder MapBulkEdit(this RouteGroupBuilder group)
    {
        group.MapPut("/bulk", async Task<Results<Ok<BulkResponse>, ProblemHttpResult>> (BulkUpdateRequest request, BulkEditService bulk, CancellationToken ct) =>
        {
            var result = await bulk.UpdateAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // Searches run in the background, one version at a time: the answer says what was queued.
        group.MapPost("/bulk/search", async Task<Results<Accepted<BulkResponse>, ProblemHttpResult>> (BulkSearchRequest request, BulkEditService bulk, CancellationToken ct) =>
        {
            var result = await bulk.SearchAsync(request, ct);
            return result.IsSuccess ? TypedResults.Accepted("/api/v1/queue", result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/bulk/delete", async Task<Results<Ok<BulkDeleteResponse>, ProblemHttpResult>> (BulkDeleteRequest request, BulkEditService bulk, CancellationToken ct) =>
        {
            var result = await bulk.DeleteAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return group;
    }
}
