using Bigarr.Domain.Events;

namespace Bigarr.Domain.Notifications;

public enum WebhookDeliveryState
{
    Pending = 1,
    Delivered = 2,

    /// <summary>Every retry failed; only a manual retry sends it again.</summary>
    GivenUp = 3,
}

/// <summary>
/// One event to one endpoint (blueprint §3.9). The row is the retry state: the sender picks up whatever is due, so a
/// delivery that failed just before a restart is tried again after it.
/// </summary>
public sealed class WebhookDelivery
{
    public long Id { get; set; }

    public long EventId { get; set; }

    public OutboxEvent Event { get; set; } = null!;

    public int EndpointId { get; set; }

    public NotificationEndpoint Endpoint { get; set; } = null!;

    /// <summary>Snapshot of the event type, for lists and filters without a join.</summary>
    public required string EventType { get; set; }

    /// <summary>
    /// For a Radarr-format delivery about a film: the version that is the Radarr movie of the payload. A film deleted
    /// under the <c>all</c> view makes one delivery per version. No foreign key: the version may be gone already.
    /// </summary>
    public int? VersionId { get; set; }

    public int Attempts { get; set; }

    /// <summary>Null once delivered or given up.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public WebhookDeliveryState State =>
        DeliveredAt is not null ? WebhookDeliveryState.Delivered
        : NextAttemptAt is null ? WebhookDeliveryState.GivenUp
        : WebhookDeliveryState.Pending;
}
