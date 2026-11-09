using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Tv;

/// <summary>
/// Identity and metadata cache of a show, like <see cref="Movie"/>: no quality, monitoring, folder or path here.
/// One row per show; what is wanted lives on its <see cref="SeriesVersion"/>s.
/// </summary>
public sealed class Series
{
    public int Id { get; set; }

    /// <summary>The ecosystem's key (releases, Seerr, Sonarr, Torznab <c>tvdbid</c>). Null only for a show TVDB lacks.</summary>
    public int? TvdbId { get; set; }

    public int? TmdbId { get; set; }

    public string? ImdbId { get; set; }

    public required string Title { get; set; }

    public string? OriginalTitle { get; set; }

    public required string SortTitle { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public SeriesStatus Status { get; set; } = SeriesStatus.Continuing;

    public string? Network { get; set; }

    /// <summary>Typical episode length in minutes.</summary>
    public int? Runtime { get; set; }

    /// <summary>Local air time as the provider gives it (<c>21:00</c>), in the time zone of <see cref="OriginalCountry"/>.</summary>
    public string? AirTime { get; set; }

    /// <summary>ISO 3166-1 alpha-2, lower case (<c>us</c>, <c>jp</c>).</summary>
    public string? OriginalCountry { get; set; }

    /// <summary>ISO 639-1 code.</summary>
    public string? OriginalLanguage { get; set; }

    public SeriesType SeriesType { get; set; } = SeriesType.Standard;

    /// <summary>Numbering in use (blueprint §8). TVDB only for now; the per-show switch comes later.</summary>
    public EpisodeOrder Ordering { get; set; } = EpisodeOrder.Tvdb;

    public List<string> Genres { get; set; } = [];

    /// <summary>Feeds the parser's matching (and the compat <c>alternateTitles</c>).</summary>
    public List<AlternativeTitle> AlternativeTitles { get; set; } = [];

    public List<MediaImage> Images { get; set; } = [];

    public DateOnly? FirstAired { get; set; }

    public DateOnly? LastAired { get; set; }

    /// <summary>Only the value inherited when a version is created. Never used to resolve an existing file's path.</summary>
    public int DefaultRootFolderId { get; set; }

    public RootFolder DefaultRootFolder { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastMetadataRefreshAt { get; set; }

    public ICollection<SeriesVersion> Versions { get; set; } = [];

    public ICollection<Season> Seasons { get; set; } = [];

    public ICollection<Episode> Episodes { get; set; } = [];

    public ICollection<Tag> Tags { get; set; } = [];

    /// <summary>Computed: at least one monitored version. Not mapped.</summary>
    public bool Monitored => Versions.Any(v => v.Monitored);

    public static Series FromMetadata(SeriesMetadata metadata, int defaultRootFolderId)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var series = new Series
        {
            Title = metadata.Title,
            SortTitle = Movies.SortTitle.Build(metadata.Title),
            DefaultRootFolderId = defaultRootFolderId,
            SeriesType = metadata.SuggestedType,
        };
        series.ApplyMetadata(metadata);
        return series;
    }

    /// <summary>Refreshes the series-level cache. Seasons and episodes are synchronised by the slice (they carry per-version state).</summary>
    public void ApplyMetadata(SeriesMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        TvdbId = metadata.TvdbId ?? TvdbId;
        TmdbId = metadata.TmdbId ?? TmdbId;
        ImdbId = metadata.ImdbId ?? ImdbId;
        Title = metadata.Title;
        OriginalTitle = metadata.OriginalTitle;
        SortTitle = Movies.SortTitle.Build(metadata.Title);
        Year = metadata.Year;
        Overview = metadata.Overview;
        Status = metadata.Status;
        Network = metadata.Network;
        Runtime = metadata.Runtime;
        AirTime = metadata.AirTime;
        OriginalCountry = metadata.OriginalCountry;
        OriginalLanguage = metadata.OriginalLanguage;
        Genres = [.. metadata.Genres];
        AlternativeTitles = [.. metadata.AlternativeTitles];
        Images = [.. metadata.Images];
        FirstAired = metadata.FirstAired;
        LastAired = metadata.LastAired;
        LastMetadataRefreshAt = DateTime.UtcNow;
    }
}
