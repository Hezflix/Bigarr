namespace Bigarr.Domain.Events;

/// <summary>
/// A business fact written in the same transaction as the change it describes (blueprint §3.9). A hosted service
/// drains the table after commit and hands each row to the consumers that leave the process: SSE, webhooks, later
/// the compat SignalR hub. Retries survive a restart because the row does.
/// </summary>
public sealed class OutboxEvent
{
    public long Id { get; set; }

    /// <summary>The event's camelCase name (<see cref="DomainEvent.Type"/>), what subscriptions match on.</summary>
    public required string Type { get; set; }

    /// <summary>The event record serialized as JSON: everything a consumer needs, so a deleted film still has a readable event.</summary>
    public required string Payload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DispatchedAt { get; set; }
}
