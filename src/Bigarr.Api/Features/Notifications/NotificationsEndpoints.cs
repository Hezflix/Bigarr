using System.Net.ServerSentEvents;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Notifications;

public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", async Task<Ok<List<NotificationEndpointResponse>>> (NotificationService notifications, CancellationToken ct) =>
            TypedResults.Ok(await notifications.ListAsync(ct)));

        group.MapGet("/events", Ok<List<NotificationEventTypeResponse>> () =>
            TypedResults.Ok(DomainEventCatalog.All
                .Select(e => new NotificationEventTypeResponse(e.Type, e.Description, WebhookRouting.RadarrEventType(e.Type), WebhookRouting.IgnoresTags(e.Type), WebhookRouting.SentToDiscord(e.Type), WebhookRouting.SonarrEventType(e.Type)))
                .ToList()));

        group.MapGet("/{id:int}", async Task<Results<Ok<NotificationEndpointResponse>, NotFound>> (int id, NotificationService notifications, CancellationToken ct) =>
        {
            var endpoint = await notifications.GetAsync(id, ct);
            return endpoint is null ? TypedResults.NotFound() : TypedResults.Ok(endpoint);
        });

        group.MapPost("/", async Task<Results<Created<NotificationEndpointResponse>, ProblemHttpResult>> (NotificationEndpointRequest request, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.AddAsync(request, ct);
            return result.IsSuccess ? TypedResults.Created($"/api/v1/notifications/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
        });

        group.MapPut("/{id:int}", async Task<Results<Ok<NotificationEndpointResponse>, ProblemHttpResult>> (int id, NotificationEndpointRequest request, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.UpdateAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapDelete("/{id:int}", async Task<Results<NoContent, ProblemHttpResult>> (int id, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.DeleteAsync(id, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // Test an unsaved configuration (the form) or a saved endpoint (the row).
        group.MapPost("/test", async Task<Results<Ok<WebhookTestResponse>, ProblemHttpResult>> (NotificationEndpointRequest request, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.TestAsync(request, null, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/{id:int}/test", async Task<Results<Ok<WebhookTestResponse>, ProblemHttpResult>> (int id, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.TestAsync(null, id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapGet("/{id:int}/deliveries", async Task<Results<Ok<List<WebhookDeliveryResponse>>, ProblemHttpResult>> (int id, int? limit, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.DeliveriesAsync(id, limit, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/deliveries/{deliveryId:long}/retry", async Task<Results<Ok<WebhookDeliveryResponse>, ProblemHttpResult>> (long deliveryId, NotificationService notifications, CancellationToken ct) =>
        {
            var result = await notifications.RetryAsync(deliveryId, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // text/event-stream of every dispatched event: the event name is the type, the data the payload (blueprint §3.9).
        app.MapGet("/api/v1/events", (EventStream stream, CancellationToken ct) =>
                TypedResults.ServerSentEvents(Items(stream, ct)))
            .WithTags("Notifications")
            .RequireAuthorization(AuthPolicies.Api);

        return app;
    }

    private static async IAsyncEnumerable<SseItem<string>> Items(EventStream stream, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var serverEvent in stream.SubscribeAsync(ct))
        {
            yield return new SseItem<string>(serverEvent.Payload, serverEvent.Type) { EventId = serverEvent.Id.ToString() };
        }
    }
}
