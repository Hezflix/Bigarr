using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>The Discord bot that sends direct messages.</summary>
/// <param name="Token">The mask when a token is stored, null otherwise: the token itself is never sent back.</param>
public sealed record DiscordBotResponse(bool Configured, string? Token);

/// <param name="Token">The mask keeps the stored token, empty removes it, anything else replaces it.</param>
public sealed record DiscordBotRequest(string? Token);

/// <summary>One user's direct message about an announcement.</summary>
public sealed record NewEpisodeMessageResponse(
    long Id,
    int UserId,
    string? Username,
    WebhookDeliveryState State,
    int Attempts,
    DateTime? DeliveredAt,
    DateTime? NextAttemptAt,
    int? LastStatusCode,
    string? LastError);

/// <summary>New episodes of a show, and who was told.</summary>
/// <param name="Episodes">As <c>S01E02</c>, in order.</param>
/// <param name="ReadyAt">When the messages were made; null while the batch collects episodes or waits for Plex.</param>
public sealed record NewEpisodeAnnouncementResponse(
    long Id,
    int SeriesId,
    string? SeriesTitle,
    List<string> Episodes,
    DateTime CreatedAt,
    DateTime? ReadyAt,
    PlexWait? PlexWait,
    List<NewEpisodeMessageResponse> Messages);

/// <summary>The signed-in user's shows, those whose new episodes they may get.</summary>
/// <param name="Linked">One of their Plex accounts plays on a server Bigarr follows: without one they follow nothing.</param>
/// <param name="BotConfigured">The admin set up the Discord bot: without it no message is sent.</param>
/// <param name="NewEpisodes">They have a Discord id and the "new episode" choice on.</param>
public sealed record FollowedShowsResponse(bool Linked, bool BotConfigured, bool NewEpisodes, List<FollowedShow> Shows);

public sealed record FollowShowRequest(bool Notify);
