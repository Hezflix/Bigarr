using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Metadata;

/// <summary>Provider-neutral snapshot of what we know about a film. Search results may leave most fields empty.</summary>
public sealed record MovieMetadata
{
    public required int TmdbId { get; init; }

    public string? ImdbId { get; init; }

    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    public int? Year { get; init; }

    /// <summary>Minutes.</summary>
    public int? Runtime { get; init; }

    public string? Overview { get; init; }

    public string? Certification { get; init; }

    /// <summary>ISO 639-1 code.</summary>
    public string? OriginalLanguage { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public MovieRatings? Ratings { get; init; }

    public IReadOnlyList<AlternativeTitle> AlternativeTitles { get; init; } = [];

    public MovieStatus Status { get; init; }

    public DateOnly? InCinemasDate { get; init; }

    public DateOnly? DigitalReleaseDate { get; init; }

    public DateOnly? PhysicalReleaseDate { get; init; }

    public int? CollectionTmdbId { get; init; }

    public IReadOnlyList<MediaImage> Images { get; init; } = [];
}
