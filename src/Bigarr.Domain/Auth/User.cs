namespace Bigarr.Domain.Auth;

public sealed class User
{
    public int Id { get; set; }

    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Admin;

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
