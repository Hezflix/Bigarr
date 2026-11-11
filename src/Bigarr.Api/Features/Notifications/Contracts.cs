using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications;

/// <summary>Shared by create and update.</summary>
public sealed record NotificationEndpointRequest(
    string Name,
    string Url,
    string? Secret = null,
    bool Enabled = true,
    /// <summary>Event types to send; empty means every event.</summary>
    List<string>? Events = null,
    /// <summary>Tag ids: only films whose versions carry one of them. Empty means every film; health and test ignore them.</summary>
    List<int>? Tags = null,
    WebhookFormat Format = WebhookFormat.Bigarr,
    /// <summary>For the Radarr format, which versions are Radarr movies: <c>default</c>, <c>all</c> or <c>version:&lt;name&gt;</c>.</summary>
    string? View = null,
    /// <summary>For the Discord format: name, avatar, author and fields; Radarr's defaults for whatever is left out.</summary>
    DiscordSettings? Discord = null);

public sealed record NotificationEndpointResponse(
    int Id,
    string Name,
    string Url,
    bool HasSecret,
    bool Enabled,
    List<string> Events,
    List<int> Tags,
    WebhookFormat Format,
    string View,
    DiscordSettings? Discord,
    int PendingDeliveries,
    int FailedDeliveries,
    DateTime? LastDeliveredAt,
    string? LastError,
    DateTime CreatedAt);

/// <param name="RadarrEventType">What the Radarr format sends it as; null when that format does not send it.</param>
/// <param name="IgnoresTags">About no film: sent whatever the endpoint's tags.</param>
/// <param name="SentToDiscord">Whether the Discord format posts a message for it.</param>
public sealed record NotificationEventTypeResponse(string Type, string Description, string? RadarrEventType, bool IgnoresTags, bool SentToDiscord, string? SonarrEventType = null);

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
