using Bigarr.Api.Features.Tags;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary><c>/tag</c>: Seerr keeps one tag per requesting user, Bazarr maps them to its own.</summary>
public static class TagsEndpoints
{
    public static void MapCompatTags(this RouteGroupBuilder group)
    {
        group.MapGet("/tag", async Task<JsonHttpResult<List<RadarrTag>>> (TagService tags, CancellationToken ct) =>
            RadarrCompat.Ok((await tags.ListAsync(ct)).Select(t => new RadarrTag(t.Id, t.Label)).ToList()));

        group.MapGet("/tag/{id:int}", async Task<Results<JsonHttpResult<RadarrTag>, NotFound>> (int id, TagService tags, CancellationToken ct) =>
        {
            var tag = await tags.GetAsync(id, ct);
            return tag is null ? TypedResults.NotFound() : RadarrCompat.Ok(new RadarrTag(tag.Id, tag.Label));
        });

        group.MapPost("/tag", async Task<Results<JsonHttpResult<RadarrTag>, JsonHttpResult<List<RadarrValidationFailure>>>> (RadarrTagRequest request, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.AddAsync(new TagRequest(request.Label ?? string.Empty), ct);
            return result.IsSuccess
                ? RadarrCompat.Created(new RadarrTag(result.Value!.Id, result.Value.Label))
                : RadarrCompat.BadRequest("Label", result.Failure!.Detail ?? result.Failure.Title);
        });

        group.MapPut("/tag/{id:int}", async Task<Results<JsonHttpResult<RadarrTag>, JsonHttpResult<List<RadarrValidationFailure>>, NotFound>> (int id, RadarrTagRequest request, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.UpdateAsync(id, new TagRequest(request.Label ?? string.Empty), ct);
            if (result.IsSuccess)
            {
                return RadarrCompat.Ok(new RadarrTag(result.Value!.Id, result.Value.Label));
            }

            return result.Failure!.StatusCode == StatusCodes.Status404NotFound
                ? TypedResults.NotFound()
                : RadarrCompat.BadRequest("Label", result.Failure.Detail ?? result.Failure.Title);
        });

        group.MapDelete("/tag/{id:int}", async Task<Results<Ok, NotFound>> (int id, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok() : TypedResults.NotFound();
        });
    }
}
