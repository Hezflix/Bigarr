namespace Bigarr.Api.Features.Auth;

public sealed record LoginRequest(string Username, string Password, bool RememberMe = true);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="AuthKind">Authentication scheme used for this request: <c>Session</c> or <c>ApiKey</c>.</param>
public sealed record MeResponse(int Id, string Username, string Role, string AuthKind, string? CompatView);

public sealed record ApiKeyResponse(
    int Id,
    string Name,
    string KeyPrefix,
    string CompatView,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt);

/// <param name="CompatView"><c>default</c>, <c>all</c> or <c>version:&lt;slug&gt;</c>; defaults to <c>default</c>.</param>
public sealed record CreateApiKeyRequest(string Name, string? CompatView);

/// <summary>The only time the key itself is returned.</summary>
public sealed record ApiKeyCreatedResponse(int Id, string Name, string Key, string KeyPrefix, string CompatView, DateTime CreatedAt);

/// <summary>A provider offered on the login page: on, and linked to at least one user.</summary>
public sealed record ExternalProviderResponse(string Key, string Name);

/// <summary>A provider as Settings shows it, with the signed-in user's link if there is one.</summary>
public sealed record ExternalLoginSettingsResponse(string Key, string Name, bool Enabled, ExternalLinkResponse? Link);

/// <summary>The outside account linked to the signed-in user, as of its last sign-in.</summary>
public sealed record ExternalLinkResponse(string ExternalId, string? ExternalUsername, string? ThumbUrl, DateTime LinkedAt, DateTime? LastLoginAt);

public sealed record UpdateExternalProviderRequest(bool Enabled);

/// <summary>
/// A sign-in started: the browser opens <c>AuthUrl</c> (or shows <c>UserCode</c>), then polls with <c>FlowId</c> until
/// <c>ExpiresAt</c>.
/// </summary>
public sealed record ExternalFlowStartedResponse(string FlowId, string? AuthUrl, string? UserCode, DateTimeOffset ExpiresAt);

public sealed record ExternalFlowRequest(string FlowId);

public enum ExternalFlowStatus
{
    /// <summary>The person has not answered at the provider yet: poll again.</summary>
    Pending,

    /// <summary>Signed in: the session cookie is set.</summary>
    SignedIn,

    /// <summary>The account is now linked to the signed-in user.</summary>
    Linked,

    /// <summary>The sign-in ran out of time, was cancelled, or was never started by this browser.</summary>
    Expired,

    /// <summary>The provider would not vouch for anyone.</summary>
    Denied,

    /// <summary>The provider vouched for an account no user is linked to.</summary>
    UnknownAccount,

    /// <summary>The account is already linked to another user.</summary>
    LinkedElsewhere,

    /// <summary>Signing in with this provider was turned off meanwhile.</summary>
    Disabled,
}

/// <param name="Account">The provider's name for the account, when the provider vouched for one.</param>
public sealed record ExternalFlowPollResponse(ExternalFlowStatus Status, string? Account = null);
