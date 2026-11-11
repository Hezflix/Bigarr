namespace Bigarr.Domain.Tv;

/// <summary>
/// Which numbering names and displays the episodes of a series (blueprint §8). The order is a numbering layer only: an
/// episode's identity is its TVDB episode id (or TMDB id), its <see cref="Episode.SeasonNumber"/>/<see cref="Episode.EpisodeNumber"/>
/// stay TVDB's whatever the order, so switching never breaks files, history, monitoring or the Sonarr follow. The
/// alternative numbers live in <see cref="Episode.OrderSeasonNumber"/> and its siblings.
/// </summary>
public enum EpisodeOrder
{
    /// <summary>TVDB's default (aired) order: the ecosystem's numbering (releases, XEM, Sonarr).</summary>
    Tvdb = 1,

    /// <summary>TMDB's default order (its seasons, which may split or merge TVDB's).</summary>
    TmdbDefault = 2,

    /// <summary>A named TMDB episode group (<see cref="Series.EpisodeGroupId"/>).</summary>
    TmdbEpisodeGroup = 3,
}

/// <summary>TMDB's episode group types, same values as its API.</summary>
public enum EpisodeGroupType
{
    OriginalAirDate = 1,
    Absolute = 2,
    Dvd = 3,
    Digital = 4,
    StoryArc = 5,
    Production = 6,
    Tv = 7,
}

/// <summary>Which numbering a release matched its episodes on, recorded on every grab and shadow pick (blueprint §8).</summary>
public enum EpisodeNumbering
{
    /// <summary>TVDB's season/episode numbers (or air date for a daily show).</summary>
    Tvdb = 1,

    /// <summary>XEM scene numbers (TVDB order only).</summary>
    Xem = 2,

    /// <summary>The series' chosen alternative order, after TVDB found no acceptable release.</summary>
    Order = 3,

    /// <summary>Absolute numbers (anime), shared by every order.</summary>
    Absolute = 4,
}

/// <summary>An episode of the chosen alternative order with no TVDB counterpart: listed, not tracked (no identity).</summary>
public sealed record OrderOnlyEpisode(int SeasonNumber, int EpisodeNumber, int? AbsoluteNumber, string? Title, DateOnly? AirDate, int? TmdbId);
