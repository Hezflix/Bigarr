namespace Bigarr.Domain.Common;

/// <summary>Single row (id 1): values that belong to this install as a whole, generated once or set by the admin.</summary>
public sealed class InstanceSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// The <c>X-Plex-Client-Identifier</c> this install sends to plex.tv and to Plex servers: a GUID generated on first
    /// use and kept, as Radarr and Sonarr do. plex.tv lists each install as its own device under it.
    /// </summary>
    public string? PlexClientIdentifier { get; set; }

    /// <summary>The permissions a new user starts with (first Plex sign-in); the admin changes them in Settings → Users.</summary>
    public Auth.UserPermissions DefaultPermissions { get; set; } = Auth.UserPermissions.Defaults();

    /// <summary>
    /// Whether a Plex account linked to no user may sign in, creating its user, when it has access to a Plex server
    /// set up in Bigarr. On unless the admin turns it off; off: only accounts already linked to a user sign in.
    /// </summary>
    public bool PlexNewAccounts { get; set; } = true;

    /// <summary>What a new user may request over a sliding window: unlimited unless the admin sets limits.</summary>
    public Requests.RequestQuota DefaultQuota { get; set; } = new();

    /// <summary>The profile and root folder an approved request takes when the approver leaves them.</summary>
    public Requests.RequestDefaults RequestDefaults { get; set; } = new();

    /// <summary>
    /// The version name Discover shows as "4K" next to the default version ("standard"), as a 4K Seerr sees it through a
    /// <c>version:4k</c> key. Null or empty: Discover shows the default version only.
    /// </summary>
    public string? DiscoverFourKVersion { get; set; } = "4k";

    /// <summary>Days between two scheduled backups, set in System → Backups (0: no scheduled backup). Null: <c>Bigarr:Backup:IntervalDays</c>.</summary>
    public int? BackupIntervalDays { get; set; }

    /// <summary>Backups older than this many days are pruned by the scheduled job. Null: <c>Bigarr:Backup:RetentionDays</c>.</summary>
    public int? BackupRetentionDays { get; set; }

    /// <summary>
    /// The token of the Discord bot that sends direct messages (new episodes). A secret: never returned by the API,
    /// never logged. Null: no direct message is sent, and new episodes are not even collected.
    /// </summary>
    public string? DiscordBotToken { get; set; }

    /// <summary>The wording of the new-episode direct message and where its Plex link goes.</summary>
    public Notifications.NewEpisodeSettings NewEpisodes { get; set; } = new();
}
