using Bigarr.Domain.Library;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Movies;

/// <summary>Identity and metadata cache. No quality, monitoring, folder or path here: one row per film.</summary>
public sealed class Movie
{
    public int Id { get; set; }

    public int TmdbId { get; set; }

    public string? ImdbId { get; set; }

    public required string Title { get; set; }

    public string? OriginalTitle { get; set; }

    public required string SortTitle { get; set; }

    public int? Year { get; set; }

    /// <summary>Minutes.</summary>
    public int? Runtime { get; set; }

    public string? Overview { get; set; }

    public string? Certification { get; set; }

    /// <summary>ISO 639-1 code as returned by TMDB.</summary>
    public string? OriginalLanguage { get; set; }

    public List<string> Genres { get; set; } = [];

    public MovieRatings? Ratings { get; set; }

    /// <summary>Alternative titles feed the parser's matching.</summary>
    public List<AlternativeTitle> AlternativeTitles { get; set; } = [];

    public MovieStatus TmdbStatus { get; set; }

    public DateOnly? InCinemasDate { get; set; }

    public DateOnly? DigitalReleaseDate { get; set; }

    public DateOnly? PhysicalReleaseDate { get; set; }

    /// <summary>Collections come later; the column is here.</summary>
    public int? CollectionTmdbId { get; set; }

    public List<MediaImage> Images { get; set; } = [];

    /// <summary>Only the value inherited when a version is created. Never used to resolve an existing file's path.</summary>
    public int DefaultRootFolderId { get; set; }

    public RootFolder DefaultRootFolder { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastMetadataRefreshAt { get; set; }

    public ICollection<MovieVersion> Versions { get; set; } = [];

    public ICollection<Tag> Tags { get; set; } = [];

    /// <summary>Computed: at least one monitored version. Not mapped.</summary>
    public bool Monitored => Versions.Any(v => v.Monitored);
}
