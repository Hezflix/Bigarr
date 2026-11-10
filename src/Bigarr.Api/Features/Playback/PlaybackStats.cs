using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

public sealed record StatsTotalsResponse(int Plays, long Seconds, int Users, int Movies, int Episodes);

/// <summary>
/// One bar of the plays over time: a day, or the Monday of a week, or the first of a month, in the viewer's zone. The
/// same plays split two ways: films and episodes, or by their furthest decision (those without a stream in neither).
/// </summary>
public sealed record PlaysOverTimeResponse(DateOnly Date, int Movies, int Episodes, long Seconds, int DirectPlay = 0, int DirectStream = 0, int Transcode = 0);

/// <summary>
/// The plays of one hour of the day or one weekday, split the two ways the plays over time are: films and episodes, or
/// by their furthest decision (plays without a stream count in <c>Plays</c> only).
/// </summary>
public sealed record PlaysSplitResponse(int Plays, int Movies, int Episodes, int DirectPlay, int DirectStream, int Transcode);

public sealed record TopEntryResponse(string Title, int? MovieId, int? UserId, int Plays, int Users, long Seconds, DateTime LastPlayedAt);

/// <summary>The most streams at once in the period, and when it first happened.</summary>
public sealed record ConcurrencyResponse(int Streams, DateTime? At);

/// <summary>A version that plays through a transcode: a 4K file the players cannot take is a candidate for removal.</summary>
public sealed record TranscodedVersionResponse(int MovieId, string Title, int? Year, string? VersionName, int Plays, int Transcodes, DateTime LastPlayedAt);

public sealed record PlaybackStatsResponse(
    int? Days,
    int? UserId,
    string Bucket,
    StatsTotalsResponse Totals,
    List<PlaysOverTimeResponse> OverTime,
    List<PlaysSplitResponse> ByHour,
    List<PlaysSplitResponse> ByWeekday,
    List<TopEntryResponse> TopMovies,
    List<TopEntryResponse> TopShows,
    List<TopEntryResponse> TopUsers,
    List<TopEntryResponse> TopUsersByTime,
    List<TopEntryResponse> TopPlatforms,
    DecisionCountsResponse Decisions,
    ConcurrencyResponse Concurrency,
    List<TranscodedVersionResponse> TranscodedUhd);

/// <summary>
/// The dashboard: plays over time, by hour and weekday, the tops, the decisions, the busiest moment and the 4K files
/// played through a transcode, over the last N days or since the start. The period's plays and streams are read once,
/// as light rows, and counted in memory, in the viewer's time zone.
/// </summary>
public sealed class PlaybackStats(BigarrDbContext db, TimeProvider time)
{
    public const int TopCount = 10;

    public async Task<PlaybackStatsResponse> StatsAsync(int? days, int? userId, TimeZoneInfo zone, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var now = time.GetUtcNow().UtcDateTime;
        var from = days is { } d ? now.AddDays(-d) : DateTime.MinValue;
        var plays = await db.Plays.AsNoTracking()
            .Where(p => p.StartedAt >= from && (userId == null || p.UserId == userId))
            .Select(p => new PlayRow(
                p.Id,
                p.UserId,
                p.User.DisplayName ?? p.User.Name,
                p.Item.Kind,
                p.MovieId,
                p.Item.Title,
                p.Item.Year,
                p.Item.ShowTitle,
                p.VersionName,
                p.StartedAt,
                p.StoppedAt,
                p.PlayedSeconds))
            .ToListAsync(ct);
        var streams = await db.PlayStreams.AsNoTracking()
            .Where(s => s.Play.StartedAt >= from && (userId == null || s.Play.UserId == userId))
            .Select(s => new StreamRow(s.PlayId, s.StartedAt, s.StoppedAt, s.Details.PlayerPlatform ?? s.Details.PlayerProduct, s.Details.Decision, s.Details.SourceVideoResolution))
            .ToListAsync(ct);

        var local = (DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        var bucket = days switch { null => "month", > 90 => "week", _ => "day" };
        DateOnly BucketOf(DateTime utc)
        {
            var date = DateOnly.FromDateTime(local(utc));
            return bucket switch
            {
                "month" => new DateOnly(date.Year, date.Month, 1),
                "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
                _ => date,
            };
        }

        var streamsByPlay = streams.ToLookup(s => s.PlayId);
        var decisionOf = plays.ToDictionary(p => p.Id, p => streamsByPlay[p.Id].Max(s => s.Decision));
        var overTime = plays.GroupBy(p => BucketOf(p.StartedAt))
            .Select(g => new PlaysOverTimeResponse(
                g.Key,
                g.Count(p => p.Kind == MediaItemKind.Movie),
                g.Count(p => p.Kind == MediaItemKind.Episode),
                g.Sum(p => (long)p.PlayedSeconds),
                g.Count(p => decisionOf[p.Id] == StreamDecision.DirectPlay),
                g.Count(p => decisionOf[p.Id] == StreamDecision.DirectStream),
                g.Count(p => decisionOf[p.Id] == StreamDecision.Transcode)))
            .ToDictionary(r => r.Date);
        // Every bucket of the period, the empty ones too: a chart without gaps.
        var first = days is null ? (plays.Count == 0 ? BucketOf(now) : overTime.Keys.Min()) : BucketOf(from);
        var series = new List<PlaysOverTimeResponse>();
        for (var date = first; date <= BucketOf(now); date = bucket switch { "month" => date.AddMonths(1), "week" => date.AddDays(7), _ => date.AddDays(1) })
        {
            series.Add(overTime.GetValueOrDefault(date) ?? new PlaysOverTimeResponse(date, 0, 0, 0));
        }

        PlaysSplitResponse Split(IEnumerable<PlayRow> group)
        {
            var list = group.ToList();
            return new PlaysSplitResponse(
                list.Count,
                list.Count(p => p.Kind == MediaItemKind.Movie),
                list.Count(p => p.Kind == MediaItemKind.Episode),
                list.Count(p => decisionOf[p.Id] == StreamDecision.DirectPlay),
                list.Count(p => decisionOf[p.Id] == StreamDecision.DirectStream),
                list.Count(p => decisionOf[p.Id] == StreamDecision.Transcode));
        }

        var hours = plays.ToLookup(p => local(p.StartedAt).Hour);
        var weekdays = plays.ToLookup(p => ((int)local(p.StartedAt).DayOfWeek + 6) % 7); // Monday first
        var byHour = Enumerable.Range(0, 24).Select(h => Split(hours[h])).ToList();
        var byWeekday = Enumerable.Range(0, 7).Select(d => Split(weekdays[d])).ToList();

        var decisions = decisionOf.Values.ToList();

        return new PlaybackStatsResponse(
            days,
            userId,
            bucket,
            new StatsTotalsResponse(plays.Count, plays.Sum(p => (long)p.PlayedSeconds), plays.Select(p => p.UserId).Distinct().Count(), plays.Count(p => p.Kind == MediaItemKind.Movie), plays.Count(p => p.Kind == MediaItemKind.Episode)),
            series,
            byHour,
            byWeekday,
            Top(plays.Where(p => p.Kind == MediaItemKind.Movie), p => p.MovieId is { } id ? $"m{id}" : $"t{p.Title}\n{p.Year}", p => p.Year is { } y ? $"{p.Title} ({y})" : p.Title, p => p.MovieId, _ => null),
            Top(plays.Where(p => p.Kind == MediaItemKind.Episode), p => p.ShowTitle ?? p.Title, p => p.ShowTitle ?? p.Title, _ => null, _ => null),
            Top(plays, p => p.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.UserName, _ => null, p => p.UserId),
            Top(plays, p => p.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.UserName, _ => null, p => p.UserId, byTime: true),
            TopPlatforms(plays, streamsByPlay),
            new DecisionCountsResponse(
                decisions.Count(x => x == StreamDecision.DirectPlay),
                decisions.Count(x => x == StreamDecision.DirectStream),
                decisions.Count(x => x == StreamDecision.Transcode),
                decisions.Count(x => x is null)),
            Concurrency(streams),
            TranscodedUhd(plays, streamsByPlay));
    }

    /// <summary>A time zone by its IANA name (<c>Europe/Paris</c>) from the browser; UTC when unknown.</summary>
    public static TimeZoneInfo Zone(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>The <see cref="TopCount"/> most played, by number of plays or, <paramref name="byTime"/>, by watch time.</summary>
    private static List<TopEntryResponse> Top(IEnumerable<PlayRow> plays, Func<PlayRow, string> key, Func<PlayRow, string> title, Func<PlayRow, int?> movieId, Func<PlayRow, int?> userId, bool byTime = false)
    {
        var entries = plays.GroupBy(key)
            .Select(g =>
            {
                var latest = g.MaxBy(p => p.StoppedAt)!;
                return new TopEntryResponse(title(latest), movieId(latest), userId(latest), g.Count(), g.Select(p => p.UserId).Distinct().Count(), g.Sum(p => (long)p.PlayedSeconds), latest.StoppedAt);
            });
        var ordered = byTime
            ? entries.OrderByDescending(t => t.Seconds).ThenByDescending(t => t.Plays)
            : entries.OrderByDescending(t => t.Plays).ThenByDescending(t => t.Seconds);
        return ordered.ThenBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Take(TopCount).ToList();
    }

    /// <summary>A play counts for the platform of its last stream, like the Plays page shows it.</summary>
    private static List<TopEntryResponse> TopPlatforms(List<PlayRow> plays, ILookup<int, StreamRow> streams) =>
        Top(
            plays.Where(p => streams[p.Id].Any()),
            p => streams[p.Id].MaxBy(s => s.StoppedAt)!.Platform ?? string.Empty,
            p => streams[p.Id].MaxBy(s => s.StoppedAt)!.Platform ?? string.Empty,
            _ => null,
            _ => null);

    /// <summary>The most streams running at the same moment: starts and stops swept in time order, a stop before a start at the same second.</summary>
    private static ConcurrencyResponse Concurrency(List<StreamRow> streams)
    {
        var edges = streams.SelectMany(s => new[] { (At: s.StartedAt, Delta: 1), (At: s.StoppedAt, Delta: -1) })
            .OrderBy(e => e.At).ThenBy(e => e.Delta)
            .ToList();
        var running = 0;
        var best = 0;
        DateTime? at = null;
        foreach (var edge in edges)
        {
            running += edge.Delta;
            if (running > best)
            {
                best = running;
                at = edge.At;
            }
        }

        return new ConcurrencyResponse(best, at);
    }

    private static List<TranscodedVersionResponse> TranscodedUhd(List<PlayRow> plays, ILookup<int, StreamRow> streams) =>
        plays.Where(p => p.MovieId is not null && streams[p.Id].Any(s => VersionResolver.ResolutionOf(s.SourceResolution) == 2160))
            .GroupBy(p => (p.MovieId!.Value, p.VersionName))
            .Select(g => new TranscodedVersionResponse(
                g.Key.Value,
                g.First().Title,
                g.First().Year,
                g.Key.VersionName,
                g.Count(),
                g.Count(p => streams[p.Id].Any(s => s.Decision == StreamDecision.Transcode)),
                g.Max(p => p.StoppedAt)))
            .Where(v => v.Transcodes > 0)
            .OrderByDescending(v => (double)v.Transcodes / v.Plays).ThenByDescending(v => v.Transcodes)
            .Take(TopCount * 2)
            .ToList();

    private sealed record PlayRow(int Id, int UserId, string UserName, MediaItemKind Kind, int? MovieId, string Title, int? Year, string? ShowTitle, string? VersionName, DateTime StartedAt, DateTime StoppedAt, int PlayedSeconds);

    private sealed record StreamRow(int PlayId, DateTime StartedAt, DateTime StoppedAt, string? Platform, StreamDecision? Decision, string? SourceResolution);
}
