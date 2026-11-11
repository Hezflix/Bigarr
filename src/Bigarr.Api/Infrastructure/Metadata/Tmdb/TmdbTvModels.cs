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

public sealed class TmdbEpisodeGroupsResponse
{
    public List<TmdbEpisodeGroupSummary> Results { get; set; } = [];
}

public sealed class TmdbEpisodeGroupSummary
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>1 original air date, 2 absolute, 3 DVD, 4 digital, 5 story arc, 6 production, 7 TV.</summary>
    public int Type { get; set; }

    public int GroupCount { get; set; }

    public int EpisodeCount { get; set; }
}

public sealed class TmdbEpisodeGroup
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public int Type { get; set; }

    public List<TmdbEpisodeGroupPart> Groups { get; set; } = [];
}

/// <summary>One "season" of an episode group: its position and its episodes, each with its 0-based place.</summary>
public sealed class TmdbEpisodeGroupPart
{
    public string? Name { get; set; }

    public int Order { get; set; }

    public List<TmdbEpisodeGroupEpisode> Episodes { get; set; } = [];
}

public sealed class TmdbEpisodeGroupEpisode
{
    public int Id { get; set; }

    public string? Name { get; set; }

    /// <summary>TMDB default-order numbers.</summary>
    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    /// <summary>0-based place in its group part.</summary>
    public int Order { get; set; }

    public string? AirDate { get; set; }

    public int? Runtime { get; set; }
}

/// <summary>What the fixtures keep of TMDB's answers for one show (recorded with the real API, trimmed).</summary>
public sealed class TmdbOrderFixture
{
    public List<TmdbTvSeason> Seasons { get; set; } = [];

    public List<TmdbEpisodeGroupSummary> EpisodeGroups { get; set; } = [];

    public Dictionary<string, TmdbEpisodeGroup> GroupDetails { get; set; } = [];
}
