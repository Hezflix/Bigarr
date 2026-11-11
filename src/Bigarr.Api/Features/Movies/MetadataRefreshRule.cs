using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Movies;

/// <summary>Bound from <c>Bigarr:Metadata</c>: the scheduled refresh of the films' metadata (Radarr's "Refresh Movie" task).</summary>
public sealed class MetadataRefreshOptions
{
    public const string Section = "Bigarr:Metadata";

    public bool RefreshEnabled { get; set; } = true;

    public int RefreshIntervalHours { get; set; } = 24;

    public int RefreshInitialDelayMinutes { get; set; } = 10;

    /// <summary>Provider calls per second during a run, on top of the TMDB client's retry; 0 = no pacing. The first run after a Radarr import asks for every film.</summary>
    public double RefreshFilmsPerSecond { get; set; } = 4;

    /// <summary>Provider failures in a row that stop a run: TMDB is down or refuses the key, so the films left wait for the next run.</summary>
    public int RefreshMaxConsecutiveFailures { get; set; } = 10;

    /// <summary>
    /// The series refresh task's cadence. Sonarr's rule decides which shows are due (never twice within 6 hours); the
    /// task runs every hour and refreshes a bounded batch, so a library just imported is spread over many passes.
    /// </summary>
    public int SeriesRefreshIntervalHours { get; set; } = 1;

    /// <summary>Series refreshed per pass at most (each a few TVDB and TMDB calls).</summary>
    public int SeriesRefreshBatchSize { get; set; } = 50;

    public TimeSpan RefreshInterval => TimeSpan.FromHours(Math.Max(1, RefreshIntervalHours));

    public TimeSpan SeriesRefreshInterval => TimeSpan.FromHours(Math.Max(1, SeriesRefreshIntervalHours));

    public TimeSpan RefreshSpacing => RefreshFilmsPerSecond > 0 ? TimeSpan.FromSeconds(1 / RefreshFilmsPerSecond) : TimeSpan.Zero;
}

/// <summary>What the refresh rule reads of a film.</summary>
public sealed record MetadataRefreshCandidate(
    int MovieId,
    DateTime? LastRefreshAt,
    MovieStatus TmdbStatus,
    DateOnly? InCinemasDate,
    DateOnly? DigitalReleaseDate,
    DateOnly? PhysicalReleaseDate);

/// <summary>
/// Radarr's ShouldRefreshMovie, plus "never refreshed is always due" (a Radarr import leaves every film so): metadata
/// older than 180 days is refreshed, younger than 12 hours is not, and in between only the films whose dates may
/// still move: announced, in cinemas, or released less than 30 days ago.
/// </summary>
public static class MetadataRefreshRule
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);

    public static readonly TimeSpan MinAge = TimeSpan.FromHours(12);

    public static readonly TimeSpan RecentRelease = TimeSpan.FromDays(30);

    /// <summary>Radarr counts a film with no digital or physical date as released this long after its cinema release.</summary>
    public static readonly TimeSpan CinemaRun = TimeSpan.FromDays(Availability.CinemaRunDays);

    public static bool IsDue(MetadataRefreshCandidate film, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(film);

        if (film.LastRefreshAt is not { } last || last < now - MaxAge)
        {
            return true;
        }

        if (last >= now - MinAge)
        {
            return false;
        }

        return ReleaseStatus(film, now) switch
        {
            MovieStatus.Announced or MovieStatus.InCinemas => true,
            MovieStatus.Released => PhysicalRelease(film) >= now - RecentRelease,
            _ => false, // came out long ago, or deleted from TMDB
        };
    }

    /// <summary>
    /// Radarr's status, worked out from the dates only (its SkyHookProxy). Not <see cref="Availability.EffectiveStatus"/>,
    /// which falls back on the TMDB status when a film has no date at all.
    /// </summary>
    private static MovieStatus ReleaseStatus(MetadataRefreshCandidate film, DateTime now)
    {
        if (film.TmdbStatus == MovieStatus.Deleted)
        {
            return MovieStatus.Deleted;
        }

        var status = MovieStatus.Announced;
        if (At(film.InCinemasDate) is { } inCinemas && now > inCinemas)
        {
            var noDiscDate = film.PhysicalReleaseDate is null && film.DigitalReleaseDate is null;
            status = noDiscDate && now > inCinemas + CinemaRun ? MovieStatus.Released : MovieStatus.InCinemas;
        }

        if (now >= At(film.PhysicalReleaseDate) || now >= At(film.DigitalReleaseDate))
        {
            status = MovieStatus.Released;
        }

        return status;
    }

    /// <summary>Radarr's PhysicalReleaseDate(): the physical date, else the end of the cinema run, else never.</summary>
    private static DateTime PhysicalRelease(MetadataRefreshCandidate film) =>
        At(film.PhysicalReleaseDate) ?? At(film.InCinemasDate) + CinemaRun ?? DateTime.MaxValue;

    private static DateTime? At(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
