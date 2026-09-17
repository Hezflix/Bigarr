using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications;

/// <summary>Shared by create and update.</summary>
public sealed record NotificationEndpointRequest(
    string Name,
    string Url,
    string? Secret = null,
    bool Enabled = true,
    /// <summary>Event types to send; empty means every event.</summary>
    List<string>? Events = null);

public sealed record NotificationEndpointResponse(
    int Id,
    string Name,
    string Url,
    bool HasSecret,
    bool Enabled,
    List<string> Events,
    int PendingDeliveries,
    int FailedDeliveries,
    DateTime? LastDeliveredAt,
    string? LastError,
    DateTime CreatedAt);

public sealed record NotificationEventTypeResponse(string Type, string Description);

public sealed record WebhookTestResponse(int? StatusCode, int ElapsedMs);

public sealed record WebhookDeliveryResponse(
    long Id,
    long EventId,
    string EventType,
    WebhookDeliveryState State,
    int Attempts,
    DateTime? NextAttemptAt,
    DateTime? LastAttemptAt,
    DateTime? DeliveredAt,
    int? LastStatusCode,
    string? LastError,
    DateTime CreatedAt);
