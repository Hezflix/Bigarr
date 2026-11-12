namespace Bigarr.Domain.Auth;

/// <summary>One revocable key per connected app, each with its own compat view.</summary>
public sealed class ApiKey
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>SHA-256 of the key; the key itself is shown once at creation.</summary>
    public required string KeyHash { get; set; }

    /// <summary>First characters of the key, for display.</summary>
    public required string KeyPrefix { get; set; }

    public CompatView CompatView { get; set; } = CompatView.Default;

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;
}
