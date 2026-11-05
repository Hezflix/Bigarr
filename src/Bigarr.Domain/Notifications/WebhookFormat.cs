namespace Bigarr.Domain.Notifications;

/// <summary>The body a webhook endpoint receives.</summary>
public enum WebhookFormat
{
    /// <summary><c>{ eventType, eventId, occurredAt, instance, data }</c> with Bigarr's event snapshot, for every event type.</summary>
    Bigarr = 1,

    /// <summary>Radarr's webhook payloads, so tools written for Radarr work unchanged; events Radarr has no word for are not sent.</summary>
    Radarr = 2,

    /// <summary>
    /// A Discord channel webhook: Discord only takes its own message body, so events go out as embeds laid out like
    /// Radarr's Discord connection, tuned by <see cref="DiscordSettings"/>.
    /// </summary>
    Discord = 3,
}
