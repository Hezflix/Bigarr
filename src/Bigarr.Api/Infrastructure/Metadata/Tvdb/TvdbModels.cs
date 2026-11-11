using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Metadata.Tvdb;

// Wire shapes of TVDB API v4, limited to what we read. Extended records and episodes are camelCase, search hits snake_case.
// Years and ids arrive as strings, sometimes empty: kept as strings and parsed by the provider.

public sealed class TvdbEnvelope<T>
{
    public string? Status { get; set; }

    public T? Data { get; set; }

    public TvdbLinks? Links { get; set; }
}

public sealed class TvdbLinks
{
    /// <summary>A full URL, not a page number: the client counts pages itself.</summary>
    public string? Next { get; set; }

    [JsonPropertyName("total_items")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("page_size")]
    public int? PageSize { get; set; }
}

public sealed class TvdbSearchHit
{
    [JsonPropertyName("tvdb_id")]
    public string? TvdbId { get; set; }

    public string? Name { get; set; }

    public string? Year { get; set; }

    public string? Overview { get; set; }

    public Dictionary<string, string>? Overviews { get; set; }

    public Dictionary<string, string>? Translations { get; set; }

    public List<string>? Aliases { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    public string? Status { get; set; }

    public string? Network { get; set; }

    public string? Country { get; set; }

    [JsonPropertyName("primary_language")]
    public string? PrimaryLanguage { get; set; }

    [JsonPropertyName("first_air_time")]
    public string? FirstAirTime { get; set; }

    public List<string>? Genres { get; set; }

    [JsonPropertyName("remote_ids")]
    public List<TvdbRemoteId>? RemoteIds { get; set; }
}

public sealed class TvdbRemoteId
{
    public string? Id { get; set; }

    /// <summary>2 IMDb, 12 TheMovieDB.com (series), 4 official website…</summary>
    public int Type { get; set; }

    public string? SourceName { get; set; }
}

public sealed class TvdbSeriesExtended
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Slug { get; set; }

    public string? Image { get; set; }

    public string? FirstAired { get; set; }

    public string? LastAired { get; set; }

    public TvdbStatus? Status { get; set; }

    public string? OriginalCountry { get; set; }

    public string? OriginalLanguage { get; set; }

    public int? AverageRuntime { get; set; }

    public string? Overview { get; set; }

    public string? Year { get; set; }

    public string? AirsTime { get; set; }

    public int? DefaultSeasonType { get; set; }

    public TvdbCompany? OriginalNetwork { get; set; }

    public TvdbCompany? LatestNetwork { get; set; }

    public List<TvdbNamed>? Genres { get; set; }

    public List<TvdbAlias>? Aliases { get; set; }

    public List<TvdbRemoteId>? RemoteIds { get; set; }

    public List<TvdbSeason>? Seasons { get; set; }

    public TvdbTranslations? Translations { get; set; }
}

public sealed class TvdbStatus
{
    public string? Name { get; set; }
}

public sealed class TvdbCompany
{
    public string? Name { get; set; }

    /// <summary>ISO 3166-1 alpha-3 (<c>usa</c>).</summary>
    public string? Country { get; set; }
}

public sealed class TvdbNamed
{
    public string? Name { get; set; }
}

public sealed class TvdbAlias
{
    public string? Language { get; set; }

    public string? Name { get; set; }
}

public sealed class TvdbSeason
{
    public int Id { get; set; }

    public int Number { get; set; }

    public TvdbSeasonType? Type { get; set; }

    public string? Image { get; set; }
}

public sealed class TvdbSeasonType
{
    public int Id { get; set; }

    /// <summary><c>official</c> is the aired (default) order.</summary>
    public string? Type { get; set; }
}

public sealed class TvdbTranslations
{
    public List<TvdbNameTranslation>? NameTranslations { get; set; }

    public List<TvdbOverviewTranslation>? OverviewTranslations { get; set; }
}

public sealed class TvdbNameTranslation
{
    public string? Language { get; set; }

    public string? Name { get; set; }
}

public sealed class TvdbOverviewTranslation
{
    public string? Language { get; set; }

    public string? Overview { get; set; }
}

public sealed class TvdbEpisodePage
{
    public List<TvdbEpisode>? Episodes { get; set; }
}

public sealed class TvdbEpisode
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? Aired { get; set; }

    public int? Runtime { get; set; }

    public string? Image { get; set; }

    public int Number { get; set; }

    public int? AbsoluteNumber { get; set; }

    public int SeasonNumber { get; set; }
}
