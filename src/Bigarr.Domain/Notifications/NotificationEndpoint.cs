namespace Bigarr.Domain.Notifications;

/// <summary>An outgoing webhook (blueprint §3.7): where events go, signed with the secret when there is one.</summary>
public sealed class NotificationEndpoint
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Url { get; set; }

    /// <summary>HMAC-SHA256 key for the <c>X-Bigarr-Signature</c> header. Optional: a private network may not need it.</summary>
    public string? Secret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Event types subscribed (<see cref="Events.DomainEvent.Type"/>). Empty means every event.</summary>
    public List<string> Events { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool Subscribes(string eventType) => Events.Count == 0 || Events.Contains(eventType, StringComparer.Ordinal);
}
