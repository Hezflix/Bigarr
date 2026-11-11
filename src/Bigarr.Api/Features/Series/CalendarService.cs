namespace Bigarr.Api.Features.Series;

using System.Globalization;
using System.Text;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>Sonarr's calendar legend: what the episode is, for this version, at the time of the request.</summary>
public enum CalendarStatus
{
    /// <summary>Not aired yet.</summary>
    Unaired = 1,

    /// <summary>Airing now (between its air time and its end).</summary>
    OnAir = 2,

    /// <summary>Aired, monitored, no file.</summary>
    Missing = 3,

    /// <summary>A download is running for it.</summary>
    Downloading = 4,

    Downloaded = 5,

    /// <summary>Aired, not monitored, no file.</summary>
    Unmonitored = 6,
}

public sealed record CalendarEpisodeResponse(
    int SeriesId,
    string SeriesTitle,
    SeriesType SeriesType,
    string? Network,
    string? PosterUrl,
    int VersionId,
    string VersionName,
    bool IsDefaultVersion,
    int EpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber,
    string Title,
    DateOnly? AirDate,
    DateTime? AirDateUtc,
    int? Runtime,
    bool Monitored,
    bool HasFile,
    string? Quality,
    CalendarStatus Status);

/// <summary>
/// Sonarr's Calendar: the episodes airing between two dates, one row per (version, episode) pair, monitored ones only
/// unless asked, with their status in Sonarr's legend (downloaded, downloading, missing, unaired, on air, unmonitored).
/// Read as light rows; the range is bounded so a careless request cannot read the whole library.
/// </summary>
public sealed class CalendarService(BigarrDbContext db, TimeProvider time)
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(62);

    public async Task<List<CalendarEpisodeResponse>> ListAsync(DateTime start, DateTime end, bool unmonitored, int? seriesId, CancellationToken ct)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (end - start > MaxRange)
        {
            end = start + MaxRange;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var query = db.EpisodeTargets.AsNoTracking()
            .Where(t => t.Episode.AirDateUtc != null && t.Episode.AirDateUtc >= start && t.Episode.AirDateUtc < end);
        if (!unmonitored)
        {
            query = query.Where(t => t.Monitored && t.Version.Monitored);
        }

        if (seriesId is { } id)
        {
            query = query.Where(t => t.Version.SeriesId == id);
        }

        var rows = await query
            .Select(t => new
            {
                t.Version.SeriesId,
                t.Version.Series.Title,
                t.Version.Series.SeriesType,
                t.Version.Series.Network,
                t.Version.Series.Images,
                SeriesRuntime = t.Version.Series.Runtime,
                t.VersionId,
                VersionName = t.Version.Name,
                t.Version.IsDefault,
                VersionMonitored = t.Version.Monitored,
                t.EpisodeId,
                t.Episode.SeasonNumber,
                t.Episode.EpisodeNumber,
                t.Episode.AbsoluteEpisodeNumber,
                EpisodeTitle = t.Episode.Title,
                t.Episode.AirDate,
                t.Episode.AirDateUtc,
                t.Episode.Runtime,
                t.Monitored,
                HasFile = t.EpisodeFileId != null,
                Quality = t.EpisodeFile == null ? null : t.EpisodeFile.Quality.Name,
                Downloading = db.GrabTargets.Any(g => g.Active && g.TargetId == t.Id),
            })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.AirDateUtc).ThenBy(r => r.Title).ThenBy(r => r.SeasonNumber).ThenBy(r => r.EpisodeNumber).ThenByDescending(r => r.IsDefault).ThenBy(r => r.VersionId)
            .Select(r =>
            {
                var monitored = r.Monitored && r.VersionMonitored;
                var runtime = r.Runtime ?? r.SeriesRuntime ?? 30;
                var status = r.HasFile ? CalendarStatus.Downloaded
                    : r.Downloading ? CalendarStatus.Downloading
                    : r.AirDateUtc > now ? CalendarStatus.Unaired
                    : r.AirDateUtc!.Value.AddMinutes(runtime) > now ? CalendarStatus.OnAir
                    : monitored ? CalendarStatus.Missing
                    : CalendarStatus.Unmonitored;
                return new CalendarEpisodeResponse(
                    r.SeriesId,
                    r.Title,
                    r.SeriesType,
                    r.Network,
                    r.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                    r.VersionId,
                    r.VersionName,
                    r.IsDefault,
                    r.EpisodeId,
                    r.SeasonNumber,
                    r.EpisodeNumber,
                    r.AbsoluteEpisodeNumber,
                    r.EpisodeTitle,
                    r.AirDate,
                    r.AirDateUtc,
                    r.Runtime ?? r.SeriesRuntime,
                    monitored,
                    r.HasFile,
                    r.Quality,
                    status);
            })
            .ToList();
    }

    /// <summary>
    /// Sonarr's iCal feed: one event per episode (the versions of a show folded into one), from its air time for its
    /// runtime, <c>Show - S01E05 - Title</c>, a stable uid so calendar apps update rather than duplicate.
    /// </summary>
    public static string ICal(IEnumerable<CalendarEpisodeResponse> episodes, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        var text = new StringBuilder();
        text.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Bigarr//Calendar//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Bigarr TV Schedule\r\n");
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

        text.Append("END:VCALENDAR\r\n");
        return text.ToString();
    }

    private static string Stamp(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendar(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/calendar").WithTags("Series").RequireAuthorization(Infrastructure.Auth.AuthPolicies.Api);

        // ?start=2026-11-09&end=2026-11-16 (UTC, end excluded); a week from today when left out, like Sonarr.
        group.MapGet("/", async (CalendarService calendar, TimeProvider time, CancellationToken ct, DateTime? start = null, DateTime? end = null, bool unmonitored = false, int? seriesId = null) =>
        {
            var today = time.GetUtcNow().UtcDateTime.Date;
            return TypedResults.Ok(await calendar.ListAsync(Utc(start) ?? today, Utc(end) ?? today.AddDays(7), unmonitored, seriesId, ct));
        });

        // Sonarr's feed: what a calendar app subscribes to with ?apikey=, 7 days back and 28 ahead.
        group.MapGet("/bigarr.ics", async (CalendarService calendar, TimeProvider time, CancellationToken ct, int pastDays = 7, int futureDays = 28, bool unmonitored = false) =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            var episodes = await calendar.ListAsync(now.Date.AddDays(-Math.Clamp(pastDays, 0, 31)), now.Date.AddDays(Math.Clamp(futureDays, 1, 31)), unmonitored, null, ct);
            return Results.Text(CalendarService.ICal(episodes, now), "text/calendar; charset=utf-8");
        });
        return app;
    }

    private static DateTime? Utc(DateTime? value) => value is { } v ? (v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime()) : null;
}
