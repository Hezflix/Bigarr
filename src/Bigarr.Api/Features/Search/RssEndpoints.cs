using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Search;

public static class RssEndpoints
{
    public static IEndpointRouteBuilder MapRss(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rss").WithTags("RSS").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", Ok<RssStatusResponse> (RssSyncService rss) => TypedResults.Ok(rss.Status()));

        // Newest first; matched=true keeps the releases that belong to a film of the library.
        group.MapGet("/releases", Ok<IReadOnlyList<RssReleaseResponse>> (RssFeedState state, bool? matched, int limit = 200) =>
            TypedResults.Ok(state.Releases(matched, limit)));

        group.MapPost("/sync", async Task<Results<Ok<RssSyncResponse>, ProblemHttpResult>> (RssSyncService rss, CancellationToken ct) =>
        {
            var result = await rss.RunAsync(ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
