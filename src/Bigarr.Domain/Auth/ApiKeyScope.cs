namespace Bigarr.Domain.Auth;

/// <summary>Which surfaces an API key may reach. Narrower than what its owner may do, never wider.</summary>
public enum ApiKeyScope
{
    /// <summary>The Radarr- and Sonarr-compatible surfaces and the followed apps' webhooks. For connected apps; the default.</summary>
    Compat = 0,

    /// <summary>Everything <see cref="Compat"/> opens, plus Bigarr's own API. For scripts.</summary>
    Full = 1,

    /// <summary>The iCal feed only, read-only. For a calendar subscription.</summary>
    Calendar = 2,
}
