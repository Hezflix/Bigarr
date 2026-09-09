namespace Bigarr.Api.Infrastructure.Metadata;

/// <summary>Bound from <c>Bigarr:Tmdb</c>. Without an API key the fixture provider is used.</summary>
public sealed class MetadataOptions
{
    public const string Section = "Bigarr:Tmdb";

    /// <summary>TMDB v3 API key, or a v4 read access token (JWT). Environment: <c>Bigarr__Tmdb__ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";

    public string ImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p/";

    /// <summary>Language of titles and overviews.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Country whose certification is kept (ISO 3166-1).</summary>
    public string CertificationCountry { get; set; } = "US";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
