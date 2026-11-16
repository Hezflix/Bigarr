namespace Bigarr.Domain.Common;

/// <summary>Single row (id 1): values that belong to this install and are generated once, never typed by anyone.</summary>
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
}
