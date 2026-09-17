using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Downloads.Mock;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Downloads;

public static class DownloadsEndpoints
{
    public static IEndpointRouteBuilder MapDownloads(this IEndpointRouteBuilder app)
    {
        MapDownloadClients(app);
        MapQueue(app);
        return app;
    }

    private static void MapDownloadClients(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/downloadclients").WithTags("Download clients").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<DownloadClientResponse>>> (DownloadClientService clients, CancellationToken ct) =>
            TypedResults.Ok(await clients.ListAsync(ct)));

        group.MapGet("/{id:int}", async Task<Results<Ok<DownloadClientResponse>, NotFound>> (int id, DownloadClientService clients, CancellationToken ct) =>
        {
            var client = await clients.GetAsync(id, ct);
            return client is null ? TypedResults.NotFound() : TypedResults.Ok(client);
        });

        group.MapPost("/", async Task<Results<Created<DownloadClientResponse>, ProblemHttpResult>> (DownloadClientRequest request, DownloadClientService clients, CancellationToken ct) =>
        {
            var result = await clients.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/downloadclients/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/{id:int}", async Task<Results<Ok<DownloadClientResponse>, ProblemHttpResult>> (int id, DownloadClientRequest request, DownloadClientService clients, CancellationToken ct) =>
        {
            var result = await clients.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, DownloadClientService clients, CancellationToken ct) =>
        {
            var result = await clients.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // Test an unsaved configuration (the form) or a saved client (the row).
        group.MapPost("/test", async Task<Results<Ok<DownloadClientTestResponse>, ProblemHttpResult>> (DownloadClientRequest request, DownloadClientService clients, CancellationToken ct) =>
        {
            var result = await clients.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/{id:int}/test", async Task<Results<Ok<DownloadClientTestResponse>, ProblemHttpResult>> (int id, DownloadClientService clients, CancellationToken ct) =>
        {
            var result = await clients.TestAsync(null, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // The mock client is driven by hand, like a user would in qBittorrent: finish, break or delete a torrent.
        var mock = group.MapGroup("/mock/{downloadId}").WithTags("Download clients");
        mock.MapPost("/complete", Results<NoContent, NotFound> (string downloadId, MockDownloadClientState state, QueueState queue) =>
            Touch(state.Complete(downloadId), queue));
        mock.MapPost("/fail", Results<NoContent, NotFound> (string downloadId, string? message, MockDownloadClientState state, QueueState queue) =>
            Touch(state.Fail(downloadId, message ?? "Simulated failure."), queue));
        mock.MapPost("/stall", Results<NoContent, NotFound> (string downloadId, MockDownloadClientState state, QueueState queue) =>
            Touch(state.Stall(downloadId), queue));
        mock.MapDelete("/", Results<NoContent, NotFound> (string downloadId, MockDownloadClientState state, QueueState queue) =>
            Touch(state.Remove(downloadId), queue));
    }

    private static Results<NoContent, NotFound> Touch(bool found, QueueState queue)
    {
        if (!found)
        {
            return TypedResults.NotFound();
        }

        queue.RequestRefresh();
        return TypedResults.NoContent();
    }

    private static void MapQueue(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/queue").WithTags("Queue").RequireAuthorization(AuthPolicies.Api);

        // The last published projection; the SSE stream below pushes the next ones.
        group.MapGet("/", Ok<QueueResponse> (QueueState state) => TypedResults.Ok(state.Snapshot));

        // Poll the clients now and return the result: what the refresh button and the tests call.
        group.MapPost("/refresh", async Task<Ok<QueueResponse>> (QueuePoller poller, CancellationToken ct) => TypedResults.Ok(await poller.PollAsync(ct)));

        // text/event-stream: the current snapshot first, then one event per poll.
        group.MapGet("/events", (QueueState state, CancellationToken ct) => TypedResults.ServerSentEvents(state.SubscribeAsync(ct), eventType: "queue"));

        group.MapDelete("/{grabId:int}", async Task<Results<NoContent, ProblemHttpResult>> (
            int grabId, bool removeFromClient, bool deleteFiles, bool blocklist, QueueService queue, CancellationToken ct) =>
        {
            var result = await queue.RemoveGrabAsync(grabId, removeFromClient, deleteFiles, blocklist, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        group.MapDelete("/untracked/{clientId:int}/{downloadId}", async Task<Results<NoContent, ProblemHttpResult>> (
            int clientId, string downloadId, bool deleteFiles, QueueService queue, CancellationToken ct) =>
        {
            var result = await queue.RemoveUntrackedAsync(clientId, downloadId, deleteFiles, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        var grab = app.MapGroup("/api/v1/grab").WithTags("Queue").RequireAuthorization(AuthPolicies.Api);
        grab.MapPost("/", async Task<Results<Created<GrabResponse>, ProblemHttpResult>> (GrabRequest request, GrabService grabs, CancellationToken ct) =>
        {
            var result = await grabs.GrabAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created("/api/v1/queue", result.Value!) : result.Failure!.ToProblem();
        });
    }
}
