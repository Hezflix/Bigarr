using Bigarr.Api.Features.Migration;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Shadow;

/// <summary>Shadow mode: its status, the comparison with Radarr (Activity → Compare), the Radarrs it follows (Settings → Followed Radarrs).</summary>
public static class ShadowEndpoints
{
    public static IEndpointRouteBuilder MapShadow(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/shadow").WithTags("Shadow").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<ShadowStatusResponse>> (FollowedRadarrService service, CancellationToken ct) => TypedResults.Ok(await service.StatusAsync(ct)));

        // Activity → Compare. outcome is matched by name (same | different | bigarrOnly | radarrOnly); days=0 means all time.
        group.MapGet("/compare", async Task<Results<Ok<CompareResponse>, ValidationProblem>> (
            string? version, string? outcome, int? days, int? windowHours, int? page, int? pageSize, CompareService compare, CancellationToken ct) =>
        {
            CompareOutcome? kind = null;
            if (!string.IsNullOrWhiteSpace(outcome))
            {
                if (!Enum.TryParse<CompareOutcome>(outcome, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["outcome"] = [$"Unknown outcome '{outcome}'."] });
                }

                kind = parsed;
            }

            return TypedResults.Ok(await compare.CompareAsync(version, kind, days ?? 30, windowHours ?? CompareService.DefaultWindowHours, page ?? 1, pageSize ?? 50, ct));
        });

        var radarrs = group.MapGroup("/radarrs");

        radarrs.MapGet("/", async Task<Ok<List<FollowedRadarrResponse>>> (FollowedRadarrService service, CancellationToken ct) => TypedResults.Ok(await service.ListAsync(ct)));

        radarrs.MapGet("/{id:int}", async Task<Results<Ok<FollowedRadarrResponse>, NotFound>> (int id, FollowedRadarrService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } found ? TypedResults.Ok(found) : TypedResults.NotFound());

        radarrs.MapPost("/", async Task<Results<Created<FollowedRadarrResponse>, ProblemHttpResult>> (FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/shadow/radarrs/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        radarrs.MapPut("/{id:int}", async Task<Results<Ok<FollowedRadarrResponse>, ProblemHttpResult>> (int id, FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        radarrs.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // An unsaved form; a saved Radarr with its stored key (the form may change the URL, a masked key keeps the stored one).
        radarrs.MapPost("/test", async Task<Results<Ok<FollowedRadarrTestResponse>, ProblemHttpResult>> (FollowedRadarrRequest request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        radarrs.MapPost("/{id:int}/test", async Task<Results<Ok<FollowedRadarrTestResponse>, ProblemHttpResult>> (int id, FollowedRadarrRequest? request, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.TestAsync(request, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // The import's mapping screen data for a saved Radarr (its key never comes back to the browser).
        radarrs.MapPost("/{id:int}/preview", async Task<Results<Ok<RadarrImportPreviewResponse>, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.PreviewAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        radarrs.MapPost("/{id:int}/sync", async Task<Results<Ok<RadarrFollowPass>, ProblemHttpResult>> (int id, FollowedRadarrService service, CancellationToken ct) =>
        {
            var result = await service.SyncAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
