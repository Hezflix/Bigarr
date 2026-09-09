using System.Text.Json.Serialization;

namespace Bigarr.Api.Infrastructure.Metadata.Tmdb;

// Wire shapes of TMDB API v3 (snake_case), limited to what we read.

public sealed class TmdbMovieDetails
{
    public int Id { get; set; }

    public string? ImdbId { get; set; }

    public string? Title { get; set; }

    public string? OriginalTitle { get; set; }

    public string? OriginalLanguage { get; set; }

    public string? Overview { get; set; }

    public string? ReleaseDate { get; set; }

    public int? Runtime { get; set; }

    public string? Status { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public double? VoteAverage { get; set; }

    public int? VoteCount { get; set; }

    public List<TmdbGenre> Genres { get; set; } = [];

    public TmdbCollection? BelongsToCollection { get; set; }

    public TmdbAlternativeTitles? AlternativeTitles { get; set; }

    public TmdbReleaseDates? ReleaseDates { get; set; }

    public TmdbExternalIds? ExternalIds { get; set; }
}

public sealed class TmdbGenre
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public sealed class TmdbCollection
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

public sealed class TmdbAlternativeTitles
{
    public List<TmdbAlternativeTitle> Titles { get; set; } = [];
}

public sealed class TmdbAlternativeTitle
{
    [JsonPropertyName("iso_3166_1")]
    public string? Iso31661 { get; set; }

    public string? Title { get; set; }

    public string? Type { get; set; }
}

public sealed class TmdbReleaseDates
{
    public List<TmdbReleaseDatesByCountry> Results { get; set; } = [];
}

public sealed class TmdbReleaseDatesByCountry
{
    [JsonPropertyName("iso_3166_1")]
    public string? Iso31661 { get; set; }

    public List<TmdbReleaseDate> ReleaseDates { get; set; } = [];
}

public sealed class TmdbReleaseDate
{
    public string? Certification { get; set; }

    /// <summary>1 premiere, 2 theatrical (limited), 3 theatrical, 4 digital, 5 physical, 6 TV.</summary>
    public int Type { get; set; }

    public string? ReleaseDate { get; set; }
}

public sealed class TmdbExternalIds
{
    public string? ImdbId { get; set; }
}

public sealed class TmdbSearchResponse
{
    public int Page { get; set; }

    public int TotalResults { get; set; }

    public List<TmdbSearchResult> Results { get; set; } = [];
}

public sealed class TmdbSearchResult
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? OriginalTitle { get; set; }

    public string? OriginalLanguage { get; set; }

    public string? Overview { get; set; }

    public string? ReleaseDate { get; set; }

    public string? PosterPath { get; set; }

    public string? BackdropPath { get; set; }

    public double? VoteAverage { get; set; }

    public int? VoteCount { get; set; }

    public List<int> GenreIds { get; set; } = [];
}

public sealed class TmdbFindResponse
{
    public List<TmdbSearchResult> MovieResults { get; set; } = [];
}
