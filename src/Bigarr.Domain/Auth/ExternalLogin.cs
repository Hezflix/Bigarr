namespace Bigarr.Domain.Auth;

/// <summary>
/// An account at an outside identity provider (Plex…) that signs a <see cref="User"/> in. The provider only says who
/// the person is; what they may do stays on the <see cref="User"/>. One account per provider and user, and an outside
/// account belongs to one user only.
/// </summary>
public sealed class ExternalLogin
{
    public int Id { get; set; }

    /// <summary>The provider's key, e.g. <c>plex</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>The account's id at the provider (for Plex the numeric plex.tv account id, the one playback users carry).</summary>
    public required string ExternalId { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>The account's name at the provider, as of the last sign-in: only shown, never trusted.</summary>
    public string? ExternalUsername { get; set; }

    /// <summary>The account's picture at the provider, as of the last sign-in.</summary>
    public string? ThumbUrl { get; set; }

    [SetWhenAdded]
    public DateTime LinkedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}

/// <summary>Whether signing in with a provider is offered on the login page. No row means off.</summary>
public sealed class ExternalLoginProvider
{
    /// <summary>The provider's key, e.g. <c>plex</c>.</summary>
    public required string Provider { get; set; }

    public bool Enabled { get; set; }
}
