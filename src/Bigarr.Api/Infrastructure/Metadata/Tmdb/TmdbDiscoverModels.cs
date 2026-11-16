using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

// Wire shapes of TMDB API v3 read by Discover (lists, title pages, people), limited to what it shows.

public sealed class TmdbPage
{
    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int TotalResults { get; set; }

    public List<TmdbListItem> Results { get; set; } = [];
}

/// <summary>One entry of a list: a film (<c>title</c>), a series (<c>name</c>) or a person (<c>profile_path</c>).</summary>
public sealed class TmdbListItem
{
    public int Id { get; set; }

    /// <summary><c>movie</c>, <c>tv</c> or <c>person</c>; only mixed lists (trending, multi search) carry it.</summary>
    public string? MediaType { get; set; }

    public string? Title { get; set; }

    public string? Name { get; set; }

    public string? ReleaseDate { get; set; }

    public string? FirstAirDate { get; set; }

    public string? PosterPath { get; set; }

    public string? ProfilePath { get; set; }

    public string? BackdropPath { get; set; }

    public string? Overview { get; set; }

    public double? VoteAverage { get; set; }

    public double? Popularity { get; set; }

    public bool Adult { get; set; }

    public string? KnownForDepartment { get; set; }

    /// <summary>In a person's credits: the role played, or the job done.</summary>
    public string? Character { get; set; }

    public string? Job { get; set; }
}

/// <summary>TMDB's <c>/find/{id}</c>: the titles an IMDb or TVDB id stands for.</summary>
public sealed class TmdbFind
{
    public List<TmdbListItem> MovieResults { get; set; } = [];

    public List<TmdbListItem> TvResults { get; set; } = [];
}

public sealed class TmdbGenreList
{
    public List<TmdbGenre> Genres { get; set; } = [];
}

public sealed class TmdbNamed
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? LogoPath { get; set; }
}

public sealed class TmdbNamedPage
{
    public int Page { get; set; }

    public int TotalPages { get; set; }

    public List<TmdbNamed> Results { get; set; } = [];
}

public sealed class TmdbCredits
{
    public List<TmdbCastMember> Cast { get; set; } = [];

    public List<TmdbCrewMember> Crew { get; set; } = [];
}

public sealed class TmdbCastMember
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Character { get; set; }

    public string? ProfilePath { get; set; }

    public int Order { get; set; }
}

public sealed class TmdbCrewMember
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Job { get; set; }

    public string? Department { get; set; }
}

public sealed class TmdbVideos
{
    public List<TmdbVideo> Results { get; set; } = [];
}

public sealed class TmdbVideo
{
    public string? Key { get; set; }

    public string? Site { get; set; }

    public string? Type { get; set; }

    public bool Official { get; set; }

    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; set; }
}

public sealed class TmdbDiscoverMovie
{
    public int Id { get; set; }

    public string? ImdbId { get; set; }

    public string? Title { get; set; }

    public string? OriginalTitle { get; set; }

    public string? Overview { get; set; }

    public string? Tagline { get; set; }

    public string? ReleaseDate { get; set; }

    public int? Runtime { get; set; }

    public string? Status { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public double? VoteAverage { get; set; }

    public List<TmdbGenre> Genres { get; set; } = [];

    public List<TmdbNamed> ProductionCompanies { get; set; } = [];

    public TmdbCredits? Credits { get; set; }

    public TmdbVideos? Videos { get; set; }

    public TmdbPage? Similar { get; set; }

    public TmdbPage? Recommendations { get; set; }

    public TmdbReleaseDates? ReleaseDates { get; set; }
}

public sealed class TmdbDiscoverTv
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? OriginalName { get; set; }

    public string? Overview { get; set; }

    public string? Tagline { get; set; }

    public string? FirstAirDate { get; set; }

    public string? LastAirDate { get; set; }

    public string? Status { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public double? VoteAverage { get; set; }

    public List<int> EpisodeRunTime { get; set; } = [];

    public int NumberOfSeasons { get; set; }

    public int NumberOfEpisodes { get; set; }

    public List<TmdbGenre> Genres { get; set; } = [];

    public List<TmdbNamed> Networks { get; set; } = [];

    public List<TmdbNamed> CreatedBy { get; set; } = [];

    public List<TmdbDiscoverSeason> Seasons { get; set; } = [];

    public TmdbCredits? Credits { get; set; }

    public TmdbVideos? Videos { get; set; }

    public TmdbPage? Similar { get; set; }

    public TmdbPage? Recommendations { get; set; }

    public TmdbTvExternalIds? ExternalIds { get; set; }
}

public sealed class TmdbDiscoverSeason
{
    public int SeasonNumber { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? AirDate { get; set; }

    public int EpisodeCount { get; set; }

    public string? PosterPath { get; set; }
}

public sealed class TmdbPerson
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Biography { get; set; }

    public string? Birthday { get; set; }

    public string? Deathday { get; set; }

    public string? PlaceOfBirth { get; set; }

    public string? ProfilePath { get; set; }

    public string? KnownForDepartment { get; set; }

    public TmdbCombinedCredits? CombinedCredits { get; set; }
}

public sealed class TmdbCombinedCredits
{
    public List<TmdbListItem> Cast { get; set; } = [];

    public List<TmdbListItem> Crew { get; set; } = [];
}
