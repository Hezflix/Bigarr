namespace Bigarr.Domain.Notifications;

/// <summary>How an announcement stopped waiting for the media server.</summary>
public enum PlexWait
{
    /// <summary>Plex listed every episode of the batch.</summary>
    Seen = 1,

    /// <summary>Plex still did not list them when the wait ran out: sent anyway.</summary>
    TimedOut = 2,

    /// <summary>No Plex server knows the show, so nothing could be checked.</summary>
    NotChecked = 3,

    /// <summary>Nobody follows the show (or everyone opted out): nothing to wait for, nothing sent.</summary>
    NoRecipient = 4,
}

/// <summary>
/// New episodes of one show, told once to everyone who watches it, in a Discord direct message. Episodes that land
/// close together (a season pack, a few files in a row) join the same announcement while it is collecting; it closes
/// a short while after the last one, waits until Plex lists them, then gets one <see cref="NewEpisodeMessage"/> per
/// recipient. States: collecting, then waiting for Plex (both with <see cref="ReadyAt"/> null), then ready.
/// </summary>
public sealed class NewEpisodeAnnouncement
{
    public long Id { get; set; }

    /// <summary>No foreign key: the show may be deleted while its announcement waits, which then sends nothing.</summary>
    public int SeriesId { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    /// <summary>When the last episode joined: the batch closes a debounce after it.</summary>
    public DateTime LastAddedAt { get; set; }

    /// <summary>When Plex was last asked whether it lists the episodes.</summary>
    public DateTime? PlexCheckedAt { get; set; }

    /// <summary>Recipients fixed and messages created (or none to create). Null while collecting or waiting.</summary>
    public DateTime? ReadyAt { get; set; }

    public PlexWait? PlexWait { get; set; }

    /// <summary>The message every recipient gets (Discord's JSON), built once when the announcement is ready.</summary>
    public string? Body { get; set; }

    public List<NewEpisodeAnnouncementEpisode> Episodes { get; set; } = [];

    public List<NewEpisodeMessage> Messages { get; set; } = [];
}

/// <summary>
/// One episode in an announcement, a snapshot of what the message says about it. An episode is announced once, ever:
/// a second version, a re-download or an upgrade of it tells nobody again (unique on <see cref="EpisodeId"/>).
/// </summary>
public sealed class NewEpisodeAnnouncementEpisode
{
    public long Id { get; set; }

    public long AnnouncementId { get; set; }

    public NewEpisodeAnnouncement Announcement { get; set; } = null!;

    /// <summary>No foreign key: the snapshot outlives the episode.</summary>
    public int EpisodeId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Title { get; set; }

    public string? Overview { get; set; }

    /// <summary>The file as Bigarr imported it, to find it in Plex's listing of the show.</summary>
    public required string FilePath { get; set; }
}

/// <summary>
/// The announcement to one user, a Discord direct message. The row is the retry state, like a webhook delivery:
/// <c>Delivered</c> when <see cref="DeliveredAt"/> is set, <c>GivenUp</c> when it is not and <see cref="NextAttemptAt"/>
/// is null (Discord refused the user for good, or every retry failed), <c>Pending</c> otherwise.
/// </summary>
public sealed class NewEpisodeMessage
{
    public long Id { get; set; }

    public long AnnouncementId { get; set; }

    public NewEpisodeAnnouncement Announcement { get; set; } = null!;

    /// <summary>No foreign key: the history stays when a user goes.</summary>
    public int UserId { get; set; }

    /// <summary>The Discord id the message goes to, as it was when the announcement was ready.</summary>
    public required string DiscordUserId { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public WebhookDeliveryState State =>
        DeliveredAt is not null ? WebhookDeliveryState.Delivered
        : NextAttemptAt is null ? WebhookDeliveryState.GivenUp
        : WebhookDeliveryState.Pending;
}

/// <summary>
/// A user who watches a show but does not want its new episodes. Following itself is never stored: anyone with a play
/// of the show follows it.
/// </summary>
public sealed class SeriesNotificationOptOut
{
    public int UserId { get; set; }

    public int SeriesId { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }
}

/// <summary>Where the "View on Plex" link of a new-episode message opens the show.</summary>
public enum PlexLinkTarget
{
    /// <summary><c>https://app.plex.tv</c>: works for every Plex user, whatever the server's own address.</summary>
    AppPlexTv = 0,

    /// <summary>The server's own public address (<see cref="NewEpisodeSettings.PlexWebAddress"/>); app.plex.tv while it is empty.</summary>
    ServerAddress = 1,
}

/// <summary>
/// The wording of the new-episode direct message and where its Plex link goes, set by the admin in Settings →
/// Notifications. A null or blank text uses the English default; texts hold placeholders such as <c>{Show}</c>.
/// </summary>
public sealed class NewEpisodeSettings
{
    /// <summary>The line above the embed, one episode.</summary>
    public string? Content { get; set; }

    /// <summary>The embed's title, one episode.</summary>
    public string? Title { get; set; }

    /// <summary>The line above the embed, several episodes.</summary>
    public string? BatchContent { get; set; }

    /// <summary>The embed's title, several episodes.</summary>
    public string? BatchTitle { get; set; }

    /// <summary>One line per episode in the embed, several episodes.</summary>
    public string? BatchLine { get; set; }

    /// <summary>The last line when more episodes came than are listed.</summary>
    public string? BatchMore { get; set; }

    /// <summary>The heading of the link field.</summary>
    public string? LinkName { get; set; }

    /// <summary>The text of the link itself.</summary>
    public string? LinkText { get; set; }

    public PlexLinkTarget LinkTarget { get; set; } = PlexLinkTarget.AppPlexTv;

    /// <summary>The address friends open Plex Web at, e.g. <c>https://plex.example.com</c>; used with <see cref="PlexLinkTarget.ServerAddress"/>.</summary>
    public string? PlexWebAddress { get; set; }
}
