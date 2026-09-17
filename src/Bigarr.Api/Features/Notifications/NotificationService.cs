using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications;

/// <summary>Outgoing webhooks: the endpoints, what they subscribe to, and how their deliveries are doing.</summary>
public sealed class NotificationService(BigarrDbContext db, WebhookClient webhooks, WebhookSignal signal, TimeProvider time, ILogger<NotificationService> logger)
{
    public async Task<List<NotificationEndpointResponse>> ListAsync(CancellationToken ct)
    {
        var endpoints = await db.NotificationEndpoints.AsNoTracking().OrderBy(e => e.Name).ToListAsync(ct);
        var stats = await StatsAsync(endpoints.Select(e => e.Id).ToList(), ct);
        return endpoints.Select(e => ToResponse(e, stats.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<NotificationEndpointResponse?> GetAsync(int id, CancellationToken ct)
    {
        var endpoint = await db.NotificationEndpoints.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
        {
            return null;
        }

        var stats = await StatsAsync([id], ct);
        return ToResponse(endpoint, stats.GetValueOrDefault(id));
    }

    public async Task<Result<NotificationEndpointResponse>> AddAsync(NotificationEndpointRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (await db.NotificationEndpoints.AnyAsync(e => e.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Name already used", $"A notification named '{request.Name.Trim()}' already exists.");
        }

        var endpoint = new NotificationEndpoint { Name = request.Name.Trim(), Url = request.Url.Trim(), CreatedAt = time.GetUtcNow().UtcDateTime };
        Apply(endpoint, request);
        db.NotificationEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Notification endpoint {Name} added ({Url})", endpoint.Name, endpoint.Url);
        return Result<NotificationEndpointResponse>.Ok(ToResponse(endpoint, null));
    }

    public async Task<Result<NotificationEndpointResponse>> UpdateAsync(int id, NotificationEndpointRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var endpoint = await db.NotificationEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (endpoint is null)
        {
            return Failure.NotFound("Notification");
        }

        if (await db.NotificationEndpoints.AnyAsync(e => e.Id != id && e.Name == request.Name.Trim(), ct))
        {
            return Failure.Conflict("Name already used", $"A notification named '{request.Name.Trim()}' already exists.");
        }

        Apply(endpoint, request);
        await db.SaveChangesAsync(ct);
        if (endpoint.Enabled)
        {
            signal.Notify();
        }

        var stats = await StatsAsync([id], ct);
        return Result<NotificationEndpointResponse>.Ok(ToResponse(endpoint, stats.GetValueOrDefault(id)));
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.NotificationEndpoints.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Notification") : Result<bool>.Ok(true);
    }

    /// <summary>Sends a test event right away, to the saved endpoint or to an unsaved form, and reports what the receiver answered.</summary>
    public async Task<Result<WebhookTestResponse>> TestAsync(NotificationEndpointRequest? request, int? id, CancellationToken ct)
    {
        NotificationEndpoint endpoint;
        if (request is not null)
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            endpoint = new NotificationEndpoint { Name = request.Name.Trim(), Url = request.Url.Trim() };
            Apply(endpoint, request);
        }
        else
        {
            var saved = await db.NotificationEndpoints.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (saved is null)
            {
                return Failure.NotFound("Notification");
            }

            endpoint = saved;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var payload = EventJson.Serialize(new TestEvent($"Bigarr can reach {endpoint.Name}.") { OccurredAt = now });
        var result = await webhooks.PostAsync(endpoint, "test", 0, payload, now, "test", ct);
        if (!result.Success)
        {
            return new Failure(StatusCodes.Status502BadGateway, "The endpoint did not accept the test event", result.Error);
        }

        return Result<WebhookTestResponse>.Ok(new WebhookTestResponse(result.StatusCode, (int)result.Elapsed.TotalMilliseconds));
    }

    public async Task<Result<List<WebhookDeliveryResponse>>> DeliveriesAsync(int endpointId, int? limit, CancellationToken ct)
    {
        if (!await db.NotificationEndpoints.AnyAsync(e => e.Id == endpointId, ct))
        {
            return Failure.NotFound("Notification");
        }

        var take = Math.Clamp(limit ?? 50, 1, 500);
        var deliveries = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.Id)
            .Take(take)
            .ToListAsync(ct);
        return Result<List<WebhookDeliveryResponse>>.Ok(deliveries.Select(ToResponse).ToList());
    }

    /// <summary>Makes a delivery due now, given up or not; the attempt counter starts over so the escalation does too.</summary>
    public async Task<Result<WebhookDeliveryResponse>> RetryAsync(long deliveryId, CancellationToken ct)
    {
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null)
        {
            return Failure.NotFound("Delivery");
        }

        if (delivery.DeliveredAt is not null)
        {
            return Failure.Conflict("Already delivered", "This delivery was accepted by the endpoint.");
        }

        delivery.Attempts = 0;
        delivery.NextAttemptAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        signal.Notify();
        return Result<WebhookDeliveryResponse>.Ok(ToResponse(delivery));
    }

    public static Failure? Validate(NotificationEndpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure.Invalid("A name is required");
        }

        if (!Uri.TryCreate(request.Url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Failure.Invalid("Invalid URL", "The URL must be absolute and start with http:// or https://.");
        }

        var unknown = (request.Events ?? []).Where(e => !DomainEventCatalog.IsKnown(e)).ToList();
        if (unknown.Count > 0)
        {
            return Failure.Invalid("Unknown event type", $"Unknown event type(s): {string.Join(", ", unknown)}.");
        }

        return null;
    }

    private static void Apply(NotificationEndpoint endpoint, NotificationEndpointRequest request)
    {
        endpoint.Name = request.Name.Trim();
        endpoint.Url = request.Url.Trim();
        endpoint.Secret = string.IsNullOrWhiteSpace(request.Secret) ? null : request.Secret;
        endpoint.Enabled = request.Enabled;
        endpoint.Events = (request.Events ?? []).Distinct(StringComparer.Ordinal).OrderBy(e => e, StringComparer.Ordinal).ToList();
    }

    private async Task<Dictionary<int, DeliveryStats>> StatsAsync(List<int> endpointIds, CancellationToken ct)
    {
        if (endpointIds.Count == 0)
        {
            return [];
        }

        var rows = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => endpointIds.Contains(d.EndpointId))
            .GroupBy(d => d.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Pending = g.Count(d => d.DeliveredAt == null && d.NextAttemptAt != null),
                Failed = g.Count(d => d.DeliveredAt == null && d.NextAttemptAt == null),
                LastDeliveredAt = g.Max(d => d.DeliveredAt),
            })
            .ToListAsync(ct);

        var lastErrors = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => endpointIds.Contains(d.EndpointId) && d.LastError != null)
            .GroupBy(d => d.EndpointId)
            .Select(g => g.OrderByDescending(d => d.LastAttemptAt).Select(d => new { d.EndpointId, d.LastError, d.LastAttemptAt }).First())
            .ToListAsync(ct);

        var result = rows.ToDictionary(r => r.EndpointId, r => new DeliveryStats(r.Pending, r.Failed, r.LastDeliveredAt, null));
        foreach (var error in lastErrors)
        {
            // Only worth showing when nothing succeeded since.
            var stats = result.GetValueOrDefault(error.EndpointId) ?? new DeliveryStats(0, 0, null, null);
            if (stats.LastDeliveredAt is null || error.LastAttemptAt > stats.LastDeliveredAt)
            {
                result[error.EndpointId] = stats with { LastError = error.LastError };
            }
        }

        return result;
    }

    private static NotificationEndpointResponse ToResponse(NotificationEndpoint endpoint, DeliveryStats? stats) => new(
        endpoint.Id,
        endpoint.Name,
        endpoint.Url,
        !string.IsNullOrEmpty(endpoint.Secret),
        endpoint.Enabled,
        endpoint.Events,
        stats?.Pending ?? 0,
        stats?.Failed ?? 0,
        stats?.LastDeliveredAt,
        stats?.LastError,
        endpoint.CreatedAt);

    private static WebhookDeliveryResponse ToResponse(WebhookDelivery d) =>
        new(d.Id, d.EventId, d.EventType, d.State, d.Attempts, d.NextAttemptAt, d.LastAttemptAt, d.DeliveredAt, d.LastStatusCode, d.LastError, d.CreatedAt);

    private sealed record DeliveryStats(int Pending, int Failed, DateTime? LastDeliveredAt, string? LastError);
}
