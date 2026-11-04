using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.MediaServers;

public static class MediaServersEndpoints
{
    public static IEndpointRouteBuilder MapMediaServers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/mediaservers").WithTags("MediaServers").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<MediaServerResponse>>> (MediaServerService servers, CancellationToken ct) =>
            TypedResults.Ok(await servers.ListAsync(ct)));

        group.MapGet("/{id:int}", async Task<Results<Ok<MediaServerResponse>, NotFound>> (int id, MediaServerService servers, CancellationToken ct) =>
        {
            var server = await servers.GetAsync(id, ct);
            return server is null ? TypedResults.NotFound() : TypedResults.Ok(server);
        });

        group.MapPost("/", async Task<Results<Created<MediaServerResponse>, ProblemHttpResult>> (MediaServerRequest request, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/mediaservers/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/{id:int}", async Task<Results<Ok<MediaServerResponse>, ProblemHttpResult>> (int id, MediaServerRequest request, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // Test an unsaved configuration (the form) or a saved server (the row).
        group.MapPost("/test", async Task<Results<Ok<MediaServerTestResponse>, ProblemHttpResult>> (MediaServerRequest request, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/{id:int}/test", async Task<Results<Ok<MediaServerTestResponse>, ProblemHttpResult>> (int id, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.TestAsync(null, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/{id:int}/refresh", async Task<Results<Ok<MediaServerResponse>, ProblemHttpResult>> (int id, MediaServerService servers, CancellationToken ct) =>
        {
            var result = await servers.RefreshAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
