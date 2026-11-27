using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Domain.Notifications;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>The Discord bot that sends direct messages, and the message it sends about new episodes.</summary>
/// <param name="Token">The mask when a token is stored, null otherwise: the token itself is never sent back.</param>
/// <param name="Texts">Each text of the message: its English default and the placeholders it takes.</param>
public sealed record DiscordBotResponse(bool Configured, string? Token, NewEpisodeMessageSettings Message, List<NewEpisodeTextInfo> Texts);

public sealed record NewEpisodeTextInfo(NewEpisodeText Field, string Default, List<string> Placeholders);

/// <summary>The wording of the new-episode message and where its Plex link goes. A null or blank text uses the default.</summary>
/// <param name="PlexWebAddress">The address friends open Plex Web at, used when <paramref name="LinkTarget"/> is the server's address.</param>
public sealed record NewEpisodeMessageSettings(
    string? Content,
    string? Title,
    string? BatchContent,
    string? BatchTitle,
    string? BatchLine,
    string? BatchMore,
    string? LinkName,
    string? LinkText,
    PlexLinkTarget LinkTarget,
    string? PlexWebAddress)
{
    public static NewEpisodeMessageSettings From(NewEpisodeSettings s) =>
        new(s.Content, s.Title, s.BatchContent, s.BatchTitle, s.BatchLine, s.BatchMore, s.LinkName, s.LinkText, s.LinkTarget, s.PlexWebAddress);

    /// <summary>Blank texts become null, so they follow the default.</summary>
    public NewEpisodeSettings ToSettings() => new()
    {
        Content = Clean(Content),
        Title = Clean(Title),
        BatchContent = Clean(BatchContent),
        BatchTitle = Clean(BatchTitle),
        BatchLine = Clean(BatchLine),
        BatchMore = Clean(BatchMore),
        LinkName = Clean(LinkName),
        LinkText = Clean(LinkText),
        LinkTarget = LinkTarget,
        PlexWebAddress = Clean(PlexWebAddress)?.Trim(),
    };

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

/// <summary>The message as Discord will show it, for one episode and for several, with what is wrong in the texts.</summary>
/// <param name="AddressInvalid">The server's address is chosen and filled but is not an http(s) URL: links go to app.plex.tv.</param>
public sealed record NewEpisodePreviewResponse(DiscordPayload Single, DiscordPayload Batch, List<TemplateProblem> Problems, bool AddressInvalid);

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
