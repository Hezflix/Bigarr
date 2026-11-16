using Bigarr.Api.Features.Requests;
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
    bool IsYou,
    QuotaDto Quota,
    UserNotificationsDto Notifications);

/// <param name="Quota">Null leaves the user's request limits as they are.</param>
/// <param name="Notifications">Null leaves the user's Discord id and mentions as they are.</param>
public sealed record UpdateUserRequest(UserRole Role, PermissionsDto Permissions, QuotaDto? Quota = null, UserNotificationsDto? Notifications = null);

/// <summary>How request and issue messages on Discord reach the user.</summary>
/// <param name="DiscordUserId">Their Discord user id (17 to 20 digits); null for none, which means never mentioned.</param>
/// <param name="Mentions">The event types they are mentioned on, among <see cref="Domain.Notifications.Mentions.Choices"/>.</param>
public sealed record UserNotificationsDto(string? DiscordUserId, List<string> Mentions)
{
    public static UserNotificationsDto From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.DiscordUserId, [.. Domain.Notifications.Mentions.Effective(user.DiscordMentions)]);
    }
}

/// <param name="ManagersOnly">Only an admin or a request manager is ever mentioned on it: a user does not see it.</param>
public sealed record MentionChoiceResponse(string Type, string Description, bool ManagersOnly);

/// <summary>The signed-in user's own notification settings, with the choices that apply to them.</summary>
public sealed record MyNotificationsResponse(string? DiscordUserId, List<string> Mentions, List<MentionChoiceResponse> Choices);

/// <param name="PlexNewAccounts">A Plex account linked to no user may sign in, creating its user, when it has access to the Plex server.</param>
/// <param name="PlexServerConfigured">A Plex server is set up in Media servers: without one no new account gets in.</param>
/// <param name="DefaultQuota">What a new user may request over a sliding window.</param>
/// <param name="MentionChoices">The events a user can be mentioned on in Discord, for the user dialog.</param>
public sealed record UserSettingsResponse(bool PlexNewAccounts, PermissionsDto DefaultPermissions, bool PlexServerConfigured, QuotaDto DefaultQuota, List<MentionChoiceResponse> MentionChoices);

/// <param name="DefaultQuota">Null leaves the default limits as they are.</param>
public sealed record UpdateUserSettingsRequest(bool PlexNewAccounts, PermissionsDto DefaultPermissions, QuotaDto? DefaultQuota = null);
