using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Metadata;

/// <summary>
/// Provider-neutral snapshot of a show: TVDB's numbering joined with TMDB's ids and images (blueprint §8). Search
/// results leave <see cref="Seasons"/> and <see cref="Episodes"/> empty.
/// </summary>
public sealed record SeriesMetadata
{
    public int? TvdbId { get; init; }

    public int? TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    public int? Year { get; init; }

    public string? Overview { get; init; }

    public SeriesStatus Status { get; init; } = SeriesStatus.Continuing;

    public string? Network { get; init; }

    public int? Runtime { get; init; }

    public string? AirTime { get; init; }

    /// <summary>ISO 3166-1 alpha-2, lower case.</summary>
    public string? OriginalCountry { get; init; }

    /// <summary>ISO 639-1.</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>ISO 3166-1 alpha-2, lower case, of the network: its time zone is the air time's (<see cref="Tv.AirTimes"/>).</summary>
    public string? NetworkCountry { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<AlternativeTitle> AlternativeTitles { get; init; } = [];

    public IReadOnlyList<MediaImage> Images { get; init; } = [];

    public DateOnly? FirstAired { get; init; }

    public DateOnly? LastAired { get; init; }

    /// <summary>A hint for the add form (Japanese animation → anime). The user decides.</summary>
    public SeriesType SuggestedType { get; init; } = SeriesType.Standard;

    /// <summary><c>tvdb+tmdb</c>, <c>tvdb</c>, <c>tmdb</c> or <c>fixtures</c>: where the numbering came from.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// The episode list is TMDB's own numbering (no TVDB answer behind it). Only right for a show TVDB does not know:
    /// for one that has a TVDB id, TMDB's list must never replace TVDB's (S1, blueprint §8).
    /// </summary>
    public bool NumberedByTmdb => string.Equals(Source, "tmdb", StringComparison.Ordinal);

    public IReadOnlyList<SeasonMetadata> Seasons { get; init; } = [];

    public IReadOnlyList<EpisodeMetadata> Episodes { get; init; } = [];
}

public sealed record SeasonMetadata(int SeasonNumber, string? Title = null, string? Overview = null, string? PosterUrl = null);

public sealed record EpisodeMetadata
{
    public int? TvdbId { get; init; }

    public int? TmdbId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public int? AbsoluteEpisodeNumber { get; init; }

    public DateOnly? AirDate { get; init; }

    public required string Title { get; init; }

    public string? Overview { get; init; }

    public int? Runtime { get; init; }

    public string? ImageUrl { get; init; }
}
