namespace Bigarr.Api.Features.Movies;

using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

/// <summary>Which of a film's TMDB dates an entry of the calendar stands for, Radarr's three.</summary>
public enum ReleaseType
{
    InCinemas = 1,
    Digital = 2,
    Physical = 3,
}

/// <summary>
/// One release date of one version. <see cref="Status"/> is the library's verdict for the version today (the chip colour
/// of the library list), not a status of the date itself: a film already on disk shows its later Blu-ray date as available.
/// </summary>
public sealed record CalendarMovieResponse(
    int MovieId,
    string Title,
    int? Year,
    string? PosterUrl,
    int VersionId,
    string VersionName,
    bool IsDefaultVersion,
    ReleaseType ReleaseType,
    DateOnly Date,
    bool Monitored,
    bool HasFile,
    string? Quality,
    VersionStatus Status);

/// <summary>
/// Radarr's Calendar for Bigarr's versions: each cinema, digital and physical date TMDB gives a film, between two days,
/// one entry per (version, date), monitored versions only unless asked. The dates are the ones the metadata refresh
/// already keeps current; nothing is fetched here. The range is bounded like the episodes' calendar.
/// </summary>
public sealed class MovieCalendarService(BigarrDbContext db, TimeProvider time)
{
    public const int MaxRangeDays = 62;

    /// <summary>The entries dated <paramref name="start"/> to <paramref name="end"/>, the end excluded.</summary>
    public async Task<List<CalendarMovieResponse>> ListAsync(DateOnly start, DateOnly end, bool unmonitored, int? movieId, CancellationToken ct)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end.DayNumber - start.DayNumber > MaxRangeDays)
        {
            end = start.AddDays(MaxRangeDays);
        }

        var query = db.MovieVersions.AsNoTracking().Where(v =>
            (v.Movie.InCinemasDate >= start && v.Movie.InCinemasDate < end)
            || (v.Movie.DigitalReleaseDate >= start && v.Movie.DigitalReleaseDate < end)
            || (v.Movie.PhysicalReleaseDate >= start && v.Movie.PhysicalReleaseDate < end));
        if (!unmonitored)
        {
            query = query.Where(v => v.Monitored);
        }

        if (movieId is { } id)
        {
            query = query.Where(v => v.MovieId == id);
        }

        var rows = await query
            .Select(v => new
            {
                v.MovieId,
                v.Movie.Title,
                v.Movie.SortTitle,
                v.Movie.Year,
                v.Movie.Images,
                v.Movie.TmdbStatus,
                v.Movie.InCinemasDate,
                v.Movie.DigitalReleaseDate,
                v.Movie.PhysicalReleaseDate,
                VersionId = v.Id,
                VersionName = v.Name,
                v.IsDefault,
                v.Monitored,
                v.MinimumAvailability,
                HasFile = v.File != null,
                Quality = v.File == null ? null : v.File.Quality.Name,
                Downloading = db.GrabTargets.Any(g => g.Active && g.TargetId == v.Id),
            })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return rows
            .SelectMany(r =>
            {
                // The availability rules only read the status and the release dates.
                var movie = new Movie
                {
                    Title = r.Title,
                    SortTitle = r.SortTitle,
                    TmdbStatus = r.TmdbStatus,
                    InCinemasDate = r.InCinemasDate,
                    DigitalReleaseDate = r.DigitalReleaseDate,
                    PhysicalReleaseDate = r.PhysicalReleaseDate,
                };
                var status = Availability.StatusOf(movie, r.MinimumAvailability, r.HasFile, r.Downloading, today);
                var poster = r.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl;
                return Dates(r.InCinemasDate, r.DigitalReleaseDate, r.PhysicalReleaseDate)
                    .Where(d => d.Date >= start && d.Date < end)
                    .Select(d => new CalendarMovieResponse(r.MovieId, r.Title, r.Year, poster, r.VersionId, r.VersionName, r.IsDefault, d.Type, d.Date, r.Monitored, r.HasFile, r.Quality, status));
            })
            .OrderBy(e => e.Date).ThenBy(e => e.ReleaseType).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.IsDefaultVersion).ThenBy(e => e.VersionId)
            .ToList();
    }

    private static IEnumerable<(ReleaseType Type, DateOnly Date)> Dates(DateOnly? inCinemas, DateOnly? digital, DateOnly? physical)
    {
        if (inCinemas is { } cinema)
        {
            yield return (ReleaseType.InCinemas, cinema);
        }

        if (digital is { } d)
        {
            yield return (ReleaseType.Digital, d);
        }

        if (physical is { } p)
        {
            yield return (ReleaseType.Physical, p);
        }
    }
}

public static class MovieCalendarEndpoints
{
    public static IEndpointRouteBuilder MapMovieCalendar(this IEndpointRouteBuilder app)
    {
        // ?start=2026-11-09&end=2026-11-16: local days of the page, the end excluded; a week from today when left out.
        app.MapGet("/api/v1/calendar/movies", async (MovieCalendarService calendar, TimeProvider time, CancellationToken ct, DateOnly? start = null, DateOnly? end = null, bool unmonitored = false, int? movieId = null) =>
            {
                var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
                return TypedResults.Ok(await calendar.ListAsync(start ?? today, end ?? today.AddDays(7), unmonitored, movieId, ct));
            })
            .WithTags("Movies")
            .RequireAuthorization(Infrastructure.Auth.AuthPolicies.Api);
        return app;
    }
}
