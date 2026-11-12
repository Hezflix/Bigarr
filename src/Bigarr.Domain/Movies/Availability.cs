namespace Bigarr.Domain.Movies;

public enum VersionStatus
{
    /// <summary>The version's minimum availability is not reached yet: nothing to search.</summary>
    Pending = 1,

    /// <summary>Searchable, no file.</summary>
    Missing = 2,

    /// <summary>An active grab exists.</summary>
    Downloading = 3,

    /// <summary>Has a file.</summary>
    Available = 4,
}

/// <summary>Where a film stands in its release cycle, and whether a version may be searched.</summary>
public static class Availability
{
    /// <summary>
    /// Radarr's cinema run: a film with neither a digital nor a physical date counts as released this many days after its
    /// cinema release. TMDB often has no home-release date for older or foreign films.
    /// </summary>
    public const int CinemaRunDays = 90;

    /// <summary>Dates win over the TMDB status when they exist: TMDB flips to "Released" at the first theatrical release.</summary>
    public static MovieStatus EffectiveStatus(Movie movie, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (movie.TmdbStatus == MovieStatus.Deleted)
        {
            return MovieStatus.Deleted;
        }

        if (movie.PhysicalReleaseDate <= today || movie.DigitalReleaseDate <= today)
        {
            return MovieStatus.Released;
        }

        // Like Radarr: with no home-release date at all, the cinema run stands for one.
        if (movie.PhysicalReleaseDate is null && movie.DigitalReleaseDate is null && movie.InCinemasDate?.AddDays(CinemaRunDays) <= today)
        {
            return MovieStatus.Released;
        }

        if (movie.InCinemasDate <= today)
        {
            return MovieStatus.InCinemas;
        }

        if (movie.InCinemasDate is not null || movie.DigitalReleaseDate is not null || movie.PhysicalReleaseDate is not null)
        {
            return MovieStatus.Announced;
        }

        // No dates at all: trust TMDB, but "Released" without any date is only worth "in cinemas".
        return movie.TmdbStatus == MovieStatus.Released ? MovieStatus.InCinemas : movie.TmdbStatus;
    }

    public static bool IsAvailable(Movie movie, MinimumAvailability minimum, DateOnly today)
    {
        var status = EffectiveStatus(movie, today);
        return minimum switch
        {
            MinimumAvailability.Announced => status != MovieStatus.Deleted,
            MinimumAvailability.InCinemas => status is MovieStatus.InCinemas or MovieStatus.Released,
            MinimumAvailability.Released => status == MovieStatus.Released,
            _ => false,
        };
    }

    public static VersionStatus StatusOf(MovieVersion version, bool hasActiveGrab, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(version);
        return StatusOf(version.Movie, version.MinimumAvailability, version.File is not null, hasActiveGrab, today);
    }

    /// <summary>The same verdict from loose values, for reads that project columns instead of loading versions.</summary>
    public static VersionStatus StatusOf(Movie movie, MinimumAvailability minimum, bool hasFile, bool hasActiveGrab, DateOnly today)
    {
        if (hasFile)
        {
            return VersionStatus.Available;
        }

        if (hasActiveGrab)
        {
            return VersionStatus.Downloading;
        }

        return IsAvailable(movie, minimum, today) ? VersionStatus.Missing : VersionStatus.Pending;
    }
}
