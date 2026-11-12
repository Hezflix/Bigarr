using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Tv;

/// <summary>How a special was recognised as a film.</summary>
public enum SpecialMovieMatch
{
    None = 0,

    /// <summary>TVDB flags the episode as a movie (and usually links the movie, whose remote ids give TMDB's).</summary>
    TvdbMovie = 1,

    /// <summary>Not flagged, but long enough and a TMDB film has its title, year and runtime.</summary>
    TitleAndRuntime = 2,
}

/// <summary>Where a placement came from.</summary>
public enum FilmPlacementSource
{
    None = 0,

    /// <summary>TVDB's <c>airsAfterSeason</c> / <c>airsBeforeSeason</c>.</summary>
    Tvdb = 1,

    /// <summary>The film's date against the seasons' last air dates (Scryer's rule).</summary>
    AirDate = 2,
}

/// <summary>
/// A special of a series that is a film of the library (S8, after Scryer's <c>series_movie_links</c>). The film engine
/// owns it: the movie's default version is searched, grabbed, imported and upgraded like any film, and the special is a
/// hardlink of that file in each series version that monitors it. One link per episode; a film may be the special of
/// several shows.
/// </summary>
public sealed class SeriesMovieLink
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public int EpisodeId { get; set; }

    public Episode Episode { get; set; } = null!;

    public int MovieId { get; set; }

    public Movie Movie { get; set; } = null!;

    /// <summary>"After season N", when known.</summary>
    public int? AfterSeason { get; set; }

    /// <summary>"Before season N", when the metadata places it so (and no after-season is known).</summary>
    public int? BeforeSeason { get; set; }

    public FilmPlacementSource PlacementSource { get; set; }

    /// <summary>Added by the auto-add setting rather than by hand.</summary>
    public bool AutoAdded { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    /// <summary>Why the special could not be hardlinked last time (different filesystems…); never copied instead. Null when fine.</summary>
    public string? LinkError { get; set; }

    public DateTime? LinkErrorAt { get; set; }
}
