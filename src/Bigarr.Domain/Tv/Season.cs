using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Tv;

/// <summary>A season of the series in the order in use (TVDB). Shared by every version; season 0 holds the specials.</summary>
public sealed class Season
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public int SeasonNumber { get; set; }

    public string? Title { get; set; }

    public string? Overview { get; set; }

    public List<MediaImage> Images { get; set; } = [];

    public bool IsSpecials => SeasonNumber == 0;
}

/// <summary>
/// Per-version season flag, like Sonarr's <c>seasons[].monitored</c>: toggling it cascades to the season's episodes
/// for that version, and it decides whether episodes that appear later in the season are monitored.
/// </summary>
public sealed class SeriesVersionSeason
{
    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    public int SeasonId { get; set; }

    public Season Season { get; set; } = null!;

    public bool Monitored { get; set; }
}
