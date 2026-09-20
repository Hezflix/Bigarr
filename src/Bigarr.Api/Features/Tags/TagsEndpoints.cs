using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Tags;

public static class TagsEndpoints
{
    public static IEndpointRouteBuilder MapTags(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tags").WithTags("Tags").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<TagResponse>>> (TagService tags, CancellationToken ct) => TypedResults.Ok(await tags.ListAsync(ct)));

        group.MapGet("/{id:int}", async Task<Results<Ok<TagResponse>, NotFound>> (int id, TagService tags, CancellationToken ct) =>
        {
            var tag = await tags.GetAsync(id, ct);
            return tag is null ? TypedResults.NotFound() : TypedResults.Ok(tag);
        });

        group.MapPost("/", async Task<Results<Created<TagResponse>, ProblemHttpResult>> (TagRequest request, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/tags/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/{id:int}", async Task<Results<Ok<TagResponse>, ProblemHttpResult>> (int id, TagRequest request, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        return app;
    }
}
