namespace Bigarr.Domain.Auth;

public sealed class User
{
    public int Id { get; set; }

    public required string Username { get; set; }

    /// <summary>Empty for a user who has no password (created by signing in with Plex): the password login refuses them.</summary>
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>What the user may do beside their role; ignored for an admin, who may do everything.</summary>
    public UserPermissions Permissions { get; set; } = new();

    /// <summary>How many films and seasons the user may request over a sliding window; unlimited by default. Ignored for an admin.</summary>
    public Requests.RequestQuota Quota { get; set; } = new();

    /// <summary>When the admin turned the user off: no sign-in, and every session and API key of theirs stops working.</summary>
    public DateTime? DisabledAt { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>The user's Discord user id (a snowflake), so request and issue messages on Discord mention them.</summary>
    public string? DiscordUserId { get; set; }

    /// <summary>
    /// The events the user wants to be mentioned on (<see cref="Notifications.Mentions.Choices"/>); null until they choose,
    /// which means every one of them.
    /// </summary>
    public List<string>? DiscordMentions { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];

    public bool IsDisabled => DisabledAt is not null;

    public bool HasPassword => PasswordHash.Length > 0;

    /// <summary>What the user may actually do: everything for an admin, their stored permissions otherwise.</summary>
    public UserPermissions Effective => Role == UserRole.Admin ? UserPermissions.All() : Permissions;
}
