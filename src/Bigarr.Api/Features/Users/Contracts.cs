using Bigarr.Domain.Auth;

namespace Bigarr.Api.Features.Users;

public sealed record PermissionsDto(bool RequestFilms, bool RequestSeries, bool Request4K, bool AutoApproveFilms, bool AutoApproveSeries, bool ReportIssues)
{
    public static PermissionsDto From(UserPermissions p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new(p.RequestFilms, p.RequestSeries, p.Request4K, p.AutoApproveFilms, p.AutoApproveSeries, p.ReportIssues);
    }

    public UserPermissions ToDomain() => new()
    {
        RequestFilms = RequestFilms,
        RequestSeries = RequestSeries,
        Request4K = Request4K,
        AutoApproveFilms = AutoApproveFilms,
        AutoApproveSeries = AutoApproveSeries,
        ReportIssues = ReportIssues,
    };
}

/// <summary>An outside account the user signs in with, as of its last sign-in.</summary>
public sealed record UserLoginResponse(string Provider, string? ExternalUsername, string? Email, string? ThumbUrl, DateTime? LastLoginAt);

/// <param name="Permissions">As stored; an admin may do everything whatever they say.</param>
/// <param name="HasPassword">Signs in with a password (a user created by a Plex sign-in has none).</param>
/// <param name="IsYou">The admin looking at the list: their own role and state cannot be changed from here.</param>
public sealed record UserResponse(
    int Id,
    string Username,
    UserRole Role,
    PermissionsDto Permissions,
    DateTime? DisabledAt,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool HasPassword,
    IReadOnlyList<UserLoginResponse> Logins,
    bool IsYou);

public sealed record UpdateUserRequest(UserRole Role, PermissionsDto Permissions);

/// <param name="PlexNewAccounts">A Plex account linked to no user may sign in, creating its user, when it has access to the Plex server.</param>
/// <param name="PlexServerConfigured">A Plex server is set up in Media servers: without one no new account gets in.</param>
public sealed record UserSettingsResponse(bool PlexNewAccounts, PermissionsDto DefaultPermissions, bool PlexServerConfigured);

public sealed record UpdateUserSettingsRequest(bool PlexNewAccounts, PermissionsDto DefaultPermissions);
