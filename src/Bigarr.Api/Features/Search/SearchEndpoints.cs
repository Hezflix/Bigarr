using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization(AuthPolicies.Api);

        // Interactive search: one round of indexer queries for the film, judged by every version (or one).
        group.MapGet("/", async Task<Results<Ok<SearchResponse>, ValidationProblem, ProblemHttpResult>> (
            int? movieId, int? versionId, InteractiveSearchService search, CancellationToken ct) =>
        {
            if (movieId is null or <= 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["movieId"] = ["A movie id is required."] });
            }

            var result = await search.SearchAsync(movieId.Value, versionId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
