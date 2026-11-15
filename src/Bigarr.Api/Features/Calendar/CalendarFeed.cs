namespace Bigarr.Api.Features.Calendar;

using System.Globalization;
using System.Text;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Series;
using Bigarr.Domain.Tv;

/// <summary>
/// The iCal feed a calendar app subscribes to, the Calendar page's two sides in one: Sonarr's episode events (from the
/// air time, for the runtime) and Radarr's film events (all day, one per cinema, digital and physical date). Each event
/// has a stable uid so calendar apps update it rather than add a copy; a show's or a film's versions fold into one event.
/// </summary>
public static class CalendarFeed
{
    public static string ICal(IEnumerable<CalendarEpisodeResponse> episodes, IEnumerable<CalendarMovieResponse> films, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(films);
        var text = new StringBuilder();
        text.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Bigarr//Calendar//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Bigarr\r\n");
        foreach (var episode in episodes.GroupBy(e => e.EpisodeId).Select(g => g.OrderByDescending(e => e.IsDefaultVersion).First()))
        {
            var startAt = episode.AirDateUtc!.Value;
            var endAt = startAt.AddMinutes(episode.Runtime ?? 30);
            var label = episode.SeriesType == SeriesType.Daily && episode.AirDate is { } day
                ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
            text.Append("BEGIN:VEVENT\r\n")
                .Append(CultureInfo.InvariantCulture, $"UID:bigarr-episode-{episode.EpisodeId}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTSTAMP:{Stamp(now)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTSTART:{Stamp(startAt)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTEND:{Stamp(endAt)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"SUMMARY:{Escape($"{episode.SeriesTitle} - {label} - {episode.Title}")}\r\n")
                .Append(CultureInfo.InvariantCulture, $"STATUS:{(episode.Status == CalendarStatus.Downloaded ? "CONFIRMED" : "TENTATIVE")}\r\n");
            if (!string.IsNullOrWhiteSpace(episode.Network))
            {
                text.Append(CultureInfo.InvariantCulture, $"LOCATION:{Escape(episode.Network)}\r\n");
            }

            text.Append("END:VEVENT\r\n");
        }

        // A date without a time is an all-day event: DTEND is the next day, excluded.
        foreach (var film in films.GroupBy(f => (f.MovieId, f.ReleaseType)).Select(g => g.OrderByDescending(f => f.IsDefaultVersion).First()))
        {
            var title = film.Year is { } year ? $"{film.Title} ({year})" : film.Title;
            text.Append("BEGIN:VEVENT\r\n")
                .Append(CultureInfo.InvariantCulture, $"UID:bigarr-movie-{film.MovieId}-{Slug(film.ReleaseType)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTSTAMP:{Stamp(now)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTSTART;VALUE=DATE:{Day(film.Date)}\r\n")
                .Append(CultureInfo.InvariantCulture, $"DTEND;VALUE=DATE:{Day(film.Date.AddDays(1))}\r\n")
                .Append(CultureInfo.InvariantCulture, $"SUMMARY:{Escape($"{title} - {Label(film.ReleaseType)}")}\r\n")
                .Append(CultureInfo.InvariantCulture, $"STATUS:{(film.HasFile ? "CONFIRMED" : "TENTATIVE")}\r\n")
                .Append("TRANSP:TRANSPARENT\r\n")
                .Append("END:VEVENT\r\n");
        }

        text.Append("END:VCALENDAR\r\n");
        return text.ToString();
    }

    private static string Label(ReleaseType type) => type switch
    {
        ReleaseType.InCinemas => "In cinemas",
        ReleaseType.Digital => "Digital release",
        _ => "Physical release",
    };

    private static string Slug(ReleaseType type) => type switch
    {
        ReleaseType.InCinemas => "cinemas",
        ReleaseType.Digital => "digital",
        _ => "physical",
    };

    private static string Stamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

public static class CalendarFeedEndpoints
{
    public static IEndpointRouteBuilder MapCalendarFeed(this IEndpointRouteBuilder app)
    {
        // What a calendar app subscribes to with ?apikey=: 7 days back and 28 ahead, like Sonarr and Radarr.
        app.MapGet("/api/v1/calendar/bigarr.ics", async (CalendarService episodes, MovieCalendarService films, TimeProvider time, CancellationToken ct, int pastDays = 7, int futureDays = 28, bool unmonitored = false) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                var from = now.Date.AddDays(-Math.Clamp(pastDays, 0, 31));
                var to = now.Date.AddDays(Math.Clamp(futureDays, 1, 31));
                var shows = await episodes.ListAsync(from, to, unmonitored, null, ct);
                var movies = await films.ListAsync(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), unmonitored, null, ct);
                return Results.Text(CalendarFeed.ICal(shows, movies, now), "text/calendar; charset=utf-8");
            })
            .WithTags("Calendar")
            .RequireAuthorization(Infrastructure.Auth.AuthPolicies.Api);
        return app;
    }
}
