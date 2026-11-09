using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

// Wire shapes of TMDB API v3 for TV (snake_case), limited to what we read.

public sealed class TmdbTvDetails
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? OriginalName { get; set; }

    public string? OriginalLanguage { get; set; }

    public List<string> OriginCountry { get; set; } = [];

    public string? Overview { get; set; }

    public string? FirstAirDate { get; set; }

    public string? LastAirDate { get; set; }

    /// <summary>Returning Series, Ended, Canceled, In Production, Planned, Pilot.</summary>
    public string? Status { get; set; }

    /// <summary>Scripted, Documentary, Reality, Talk Show, News, Miniseries, Video.</summary>
    public string? Type { get; set; }

    public List<int> EpisodeRunTime { get; set; } = [];

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public List<TmdbGenre> Genres { get; set; } = [];

    public List<TmdbNetwork> Networks { get; set; } = [];

    public List<TmdbTvSeasonSummary> Seasons { get; set; } = [];

    public TmdbTvExternalIds? ExternalIds { get; set; }

    public TmdbTvAlternativeTitles? AlternativeTitles { get; set; }
}

public sealed class TmdbNetwork
{
    public string? Name { get; set; }
}

public sealed class TmdbTvSeasonSummary
{
    public int SeasonNumber { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public int EpisodeCount { get; set; }
}

public sealed class TmdbTvExternalIds
{
    public string? ImdbId { get; set; }

    public int? TvdbId { get; set; }
}

public sealed class TmdbTvAlternativeTitles
{
    /// <summary>TV alternative titles come under <c>results</c>, not <c>titles</c> as for films.</summary>
    public List<TmdbAlternativeTitle> Results { get; set; } = [];
}

public sealed class TmdbTvSeason
{
    public int SeasonNumber { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? PosterPath { get; set; }

    public List<TmdbTvEpisode> Episodes { get; set; } = [];
}

public sealed class TmdbTvEpisode
{
    public int Id { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? AirDate { get; set; }

    public int? Runtime { get; set; }

    public string? StillPath { get; set; }
}

public sealed class TmdbTvSearchResponse
{
    public List<TmdbTvSearchResult> Results { get; set; } = [];
}

public sealed class TmdbTvSearchResult
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? OriginalName { get; set; }

    public string? OriginalLanguage { get; set; }

    public List<string> OriginCountry { get; set; } = [];

    public string? Overview { get; set; }

    public string? FirstAirDate { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public List<int> GenreIds { get; set; } = [];
}

public sealed class TmdbTvFindResponse
{
    [JsonPropertyName("tv_results")]
    public List<TmdbTvSearchResult> TvResults { get; set; } = [];
}
