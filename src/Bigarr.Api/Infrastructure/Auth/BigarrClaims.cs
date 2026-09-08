namespace Bigarr.Api.Infrastructure.Auth;

public static class BigarrClaims
{
    public const string ApiKeyId = "bigarr:apikey";

    public const string CompatView = "bigarr:compatview";
}

public static class AuthSchemes
{
    /// <summary>Human session: signed HttpOnly cookie. Never opens <c>/api/v3</c>.</summary>
    public const string Session = "Session";

    /// <summary>Machine access: <c>X-Api-Key</c> header or <c>?apikey=</c>. Never opens the UI.</summary>
    public const string ApiKey = "ApiKey";
}

public static class AuthPolicies
{
    public const string Session = "Session";

    public const string ApiKey = "ApiKey";

    /// <summary>Either scheme: the v1 API is used by the SPA (cookie) and by scripts (key).</summary>
    public const string Api = "Api";
}
