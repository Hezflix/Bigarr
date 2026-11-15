using System.Security.Claims;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Decision;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/calendar</c>: Radarr's CalendarController, the films one of whose cinema, digital or physical dates falls between
/// <c>start</c> and <c>end</c> (both included, dates at midnight UTC as Radarr stores them), monitored ones unless
/// <c>unmonitored=true</c>; today and the next two days when no range is given. Same source as Bigarr's calendar.
/// </summary>
public static class CalendarEndpoints
{
    public static void MapCompatCalendar(this RouteGroupBuilder group) => group.MapGet("/calendar", ListAsync);

    private static async Task<JsonHttpResult<List<RadarrMovie>>> ListAsync(
        ClaimsPrincipal user, CompatLibrary library, MovieService movies, CustomFormatScorer scorer, CancellationToken ct, DateTime? start = null, DateTime? end = null, bool unmonitored = false)
    {
        var today = movies.Today;
        var from = start is { } s ? FirstDayFrom(Utc(s)) : today;
        var to = end is { } e ? DateOnly.FromDateTime(Utc(e)) : today.AddDays(2);
        if (to < from)
        {
            (from, to) = (to, from);
        }

        if (to.DayNumber - from.DayNumber > MovieCalendarService.MaxRangeDays)
        {
            to = from.AddDays(MovieCalendarService.MaxRangeDays);
        }

        var rows = await library.RowsAsync(
            user.CompatView(),
            m => (m.InCinemasDate >= from && m.InCinemasDate <= to)
                || (m.DigitalReleaseDate >= from && m.DigitalReleaseDate <= to)
                || (m.PhysicalReleaseDate >= from && m.PhysicalReleaseDate <= to),
            ct);
        if (!unmonitored)
        {
            rows = rows.Where(r => r.Version.Monitored).ToList();
        }

        var assessments = await RadarrMapper.AssessAsync(rows, scorer, ct);
        return RadarrCompat.Ok(rows.Select(r => RadarrMapper.ToMovie(r, today, assessments.GetValueOrDefault(r.Version.Id))).ToList());
    }

    /// <summary>The first midnight at or after the instant: a date stands at 00:00 UTC, so 10:00 on the 9th starts on the 10th.</summary>
    private static DateOnly FirstDayFrom(DateTime utc) => DateOnly.FromDateTime(utc).AddDays(utc.TimeOfDay > TimeSpan.Zero ? 1 : 0);

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
