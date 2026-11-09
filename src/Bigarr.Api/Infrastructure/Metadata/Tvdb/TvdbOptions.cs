namespace Bigarr.Api.Infrastructure.Metadata.Tvdb;

/// <summary>Bound from <c>Bigarr:Tvdb</c>. Without an API key, series numbering comes from TMDB alone (or the fixtures).</summary>
public sealed class TvdbOptions
{
    public const string Section = "Bigarr:Tvdb";

    /// <summary>TVDB v4 project API key. Logs in alone, no subscriber PIN. Environment: <c>Bigarr__Tvdb__ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api4.thetvdb.com/v4/";

    public string ArtworkBaseUrl { get; set; } = "https://artworks.thetvdb.com";

    /// <summary>ISO 639-2 code of episode titles and overviews.</summary>
    public string Language { get; set; } = "eng";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
