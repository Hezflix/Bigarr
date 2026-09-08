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
