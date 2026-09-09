namespace Bigarr.Api.Infrastructure.Indexers.Prowlarr;

/// <summary>
/// Bound from <c>Bigarr:Prowlarr</c> (env: <c>Bigarr__Prowlarr__BaseUrl</c>, <c>Bigarr__Prowlarr__ApiKey</c>).
/// When set, Prowlarr's indexers are imported as Torznab indexers (blueprint §4, ProwlarrSync, read only).
/// </summary>
public sealed class ProwlarrOptions
{
    public const string Section = "Bigarr:Prowlarr";

    /// <summary>Scheme and host, e.g. <c>http://prowlarr:9696</c>. A URL base is kept (<c>http://host/prowlarr</c>).</summary>
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Import once at startup, in the background. A manual sync is always available from the indexers page.</summary>
    public bool SyncOnStartup { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    public string NormalizedBaseUrl => (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
}
