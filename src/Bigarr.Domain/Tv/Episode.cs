namespace Bigarr.Domain.Tv;

/// <summary>
/// The episode as a fact, shared by every version of the series: its stable identity is the TVDB episode id (with the
/// TMDB id when known), its numbers are those of the order in use (TVDB). Per-version state (monitored, file, searches)
/// lives on <see cref="EpisodeTarget"/>, so the same episode id can be shown to every version.
/// </summary>
public sealed class Episode
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public int SeasonId { get; set; }

    public Season Season { get; set; } = null!;

    /// <summary>Stable key across refreshes and orders. Null only for a show TVDB lacks (TMDB alone).</summary>
    public int? TvdbId { get; set; }

    public int? TmdbId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    /// <summary>Anime numbering (TVDB's absolute number). Null for specials and shows without one.</summary>
    public int? AbsoluteEpisodeNumber { get; set; }

    /// <summary>Local air date as published (Sonarr's <c>airDate</c>); what daily releases are named after.</summary>
    public DateOnly? AirDate { get; set; }

    /// <summary>Air date and time in UTC, from the series' air time and country (Sonarr's <c>airDateUtc</c>).</summary>
    public DateTime? AirDateUtc { get; set; }

    public required string Title { get; set; }

    public string? Overview { get; set; }

    /// <summary>Minutes.</summary>
    public int? Runtime { get; set; }

    /// <summary>Still image (TMDB, else TVDB).</summary>
    public string? ImageUrl { get; set; }

    public ICollection<EpisodeTarget> Targets { get; set; } = [];

    public bool IsSpecial => SeasonNumber == 0;

    /// <summary>Aired (or airing) at <paramref name="now"/>. Without a date an episode is not considered aired.</summary>
    public bool HasAired(DateTime now) => AirDateUtc is { } utc ? utc <= now : AirDate is { } date && date.ToDateTime(TimeOnly.MinValue) <= now;
}
