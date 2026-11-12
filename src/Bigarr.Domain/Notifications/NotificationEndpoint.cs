using Bigarr.Domain.Auth;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Notifications;

/// <summary>An outgoing webhook: where events go, signed with the secret when there is one.</summary>
public sealed class NotificationEndpoint
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>A Discord webhook's URL carries its token in the path: as secret as <see cref="Secret"/>, so never logged.</summary>
    public required string Url { get; set; }

    /// <summary>
    /// HMAC-SHA256 key for the <c>X-Bigarr-Signature</c> header. Optional: a private network may not need it. Never
    /// set on a Discord endpoint, which has nothing to check it with.
    /// </summary>
    public string? Secret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Event types subscribed (<see cref="Events.DomainEvent.Type"/>). Empty means every event.</summary>
    public List<string> Events { get; set; } = [];

    /// <summary>
    /// Only films whose versions carry one of these tags, like a Radarr connection's tags. Empty means every film.
    /// Events about no film (health, test) ignore them.
    /// </summary>
    public ICollection<Tag> Tags { get; set; } = [];

    public WebhookFormat Format { get; set; } = WebhookFormat.Bigarr;

    /// <summary>For the Radarr format: which versions are Radarr movies, exactly like an API key's view.</summary>
    public CompatView View { get; set; } = CompatView.Default;

    /// <summary>For the Discord format: the webhook's name, avatar and the fields of each message. Null for the other formats.</summary>
    public DiscordSettings? Discord { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public bool Subscribes(string eventType) => Events.Count == 0 || Events.Contains(eventType, StringComparer.Ordinal);

    /// <summary>True when the endpoint has no tag or shares one with <paramref name="tagIds"/>.</summary>
    public bool Matches(IEnumerable<int> tagIds)
    {
        ArgumentNullException.ThrowIfNull(tagIds);
        return Tags.Count == 0 || tagIds.Any(id => Tags.Any(t => t.Id == id));
    }
}
