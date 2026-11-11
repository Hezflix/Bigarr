using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Blocklist;

public static class BlocklistEndpoints
{
    public static IEndpointRouteBuilder MapBlocklist(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/blocklist").WithTags("Blocklist").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<BlocklistPageResponse>> (int? movieId, int? seriesId, int? page, int? pageSize, BlocklistService blocklist, CancellationToken ct) =>
            TypedResults.Ok(await blocklist.ListAsync(movieId, page ?? 1, pageSize ?? 50, ct, seriesId)));

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, BlocklistService blocklist, CancellationToken ct) =>
        {
            var result = await blocklist.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        group.MapPost("/bulk-delete", async Task<Ok<int>> (BlocklistBulkDeleteRequest request, BlocklistService blocklist, CancellationToken ct) =>
            TypedResults.Ok(await blocklist.DeleteManyAsync(request.Ids ?? [], ct)));

        return app;
    }
}
