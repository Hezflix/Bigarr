namespace Bigarr.Api.Infrastructure.Auth;

public static class BigarrClaims
{
    public const string ApiKeyId = "bigarr:apikey";

    public const string CompatView = "bigarr:compatview";

    /// <summary>On a session cookie: a fingerprint of the password the session was opened with (<see cref="SessionStamps"/>).</summary>
    public const string SessionStamp = "bigarr:stamp";

    /// <summary>One per permission the user has (<see cref="Domain.Auth.UserPermission"/>), every one of them for an admin.</summary>
    public const string Permission = "bigarr:perm";
}

public static class AuthSchemes
{
    /// <summary>Human session: signed HttpOnly cookie. Never opens the compatible APIs (<c>/radarr</c>, <c>/sonarr</c>).</summary>
    public const string Session = "Session";

    /// <summary>Machine access: <c>X-Api-Key</c> header or <c>?apikey=</c>. Never opens the UI.</summary>
    public const string ApiKey = "ApiKey";
}

/// <summary>
/// Who may reach an endpoint. Bigarr's own pages and API are the admin's: <see cref="Session"/> and <see cref="Api"/>
/// require the admin role, and so does any endpoint that names no policy (the fallback), so nothing opens to other users
/// by accident. What other users may reach names one of the policies below on purpose.
/// </summary>
public static class AuthPolicies
{
    /// <summary>The admin's human session (cookie).</summary>
    public const string Session = "Session";

    /// <summary>An API key, for the compatible APIs and the SignalR hubs. Keys are created by the admin only.</summary>
    public const string ApiKey = "ApiKey";

    /// <summary>The admin, by cookie or key: the v1 API is used by the SPA (cookie) and by scripts (key).</summary>
    public const string Api = "Api";

    /// <summary>Any signed-in user that is not disabled, whatever the role: who am I, sign out.</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Handles other people's requests and issues: an admin or a request manager.</summary>
    public const string ManageRequests = "ManageRequests";

    /// <summary>The policy that requires <paramref name="permission"/> of a signed-in user (an admin has them all).</summary>
    public static string Has(Domain.Auth.UserPermission permission) => "Permission:" + permission;
}
