using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>
/// Keeps XEM data in the database: scene numbers on episodes (refreshed with the series metadata and on the slow
/// schedule) and scene names (global, slow schedule). TheXEM being down never fails a caller: the last known data stays.
/// </summary>
public sealed class XemService(BigarrDbContext db, IXemClient client, XemMappedIds mappedIds, IOptions<XemOptions> options, TimeProvider time, ILogger<XemService> logger)
{
    public bool Enabled => options.Value.Enabled;

    /// <summary>The slow schedule's cadence (<see cref="XemOptions.RefreshIntervalHours"/>).</summary>
    public TimeSpan RefreshInterval => TimeSpan.FromHours(Math.Max(1, options.Value.RefreshIntervalHours));

    /// <summary>How long a mapping, the list of mapped shows and the scene names are kept (<see cref="XemOptions.MapMaxAgeHours"/>).</summary>
    public TimeSpan MapMaxAge => TimeSpan.FromHours(Math.Max(1, options.Value.MapMaxAgeHours));

    /// <summary>
    /// Fetches the series' mapping and writes scene numbers onto <paramref name="episodes"/> (tracked or new; the caller
    /// saves). Returns false when nothing could be fetched. Scene numbers map onto TVDB's numbers, which every episode
    /// keeps whatever order the series shows (blueprint §8): kept current for every series.
    /// </summary>
    /// <param name="maxAge">
    /// From a metadata refresh that brought no new episode: a mapping read more recently than this is kept (the slow
    /// schedule reads every mapped show anyway), and when TheXEM's list of mapped shows cannot be read, no show is asked
    /// one by one. Null asks whenever the show is on TheXEM's list (adding a show, new episodes).
    /// </param>
    public async Task<bool> RefreshSeriesAsync(Domain.Tv.Series series, IReadOnlyCollection<Episode> episodes, CancellationToken ct, TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!Enabled || series.TvdbId is not { } tvdbId)
        {
            return false;
        }

        if (maxAge is { } age && series.SceneNumberingRefreshedAt is { } last && time.GetUtcNow().UtcDateTime - last < age)
        {
            return true;
        }

        try
        {
            var mapped = await mappedIds.GetAsync(client, logger, MapMaxAge, ct);
            if (mapped is null && maxAge is not null)
            {
                return false;
            }

            IReadOnlyList<XemMapping> mappings = mapped is not null && !mapped.Contains(tvdbId) && !series.UseSceneNumbering
                ? []
                : await client.GetMappingsAsync(tvdbId, ct);
            Apply(series, episodes, mappings);
            logger.LogDebug("XEM mapping for {Title}: {Count} row(s)", series.Title, mappings.Count);
            return true;
        }
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            // One show at a time, possibly for a whole library: the slow pass is what reports, once.
            logger.LogDebug("TheXEM unavailable for {Title} (tvdb {TvdbId}), scene numbers kept as they were: {Message}", series.Title, tvdbId, ex.Message);
            return false;
        }
    }

    /// <summary>Replaces the scene names with XEM's list. Returns the count, or null when XEM could not be read.</summary>
    public async Task<int?> RefreshNamesAsync(CancellationToken ct)
    {
        if (!Enabled)
        {
            return null;
        }

        try
        {
            return await StoreNamesAsync(ct);
        }
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            logger.LogDebug("TheXEM scene names unavailable, the previous list stays: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<int> StoreNamesAsync(CancellationToken ct)
    {
        var names = await client.GetNamesAsync(ct);
        mappedIds.NamesFetched();
        if (names.Count == 0)
        {
            return 0;
        }

        var rows = names
            .Select(n => new SceneName { TvdbId = n.TvdbId, Title = Truncate(n.Title), CleanTitle = Truncate(n.Title.CleanSeriesTitle()), SceneSeasonNumber = n.SceneSeasonNumber })
            .Where(n => n.CleanTitle.Length > 0)
            .DistinctBy(n => (n.TvdbId, n.CleanTitle, n.SceneSeasonNumber))
            .ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SceneNames.ExecuteDeleteAsync(ct);
        db.SceneNames.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return rows.Count;
    }

    /// <summary>
    /// The slow schedule, a polite pass: the scene names (unless read less than <see cref="MapMaxAge"/> ago), then the
    /// mapping of the series TheXEM has one for and that is due, the oldest (or never read) first, one request at a
    /// time through the shared throttle. Series without a mapping are never asked about. A 429 or 503 pauses the pass
    /// for the <c>Retry-After</c> and resumes; repeated refusals end it, and the next pass picks up where it stopped
    /// (the series not read yet are the oldest). Logs one line.
    /// </summary>
    public async Task<string> RefreshAllAsync(CancellationToken ct)
    {
        if (!Enabled)
        {
            return "TheXEM is disabled";
        }

        var pass = new PassState();
        var maxAge = MapMaxAge;

        string namesText;
        if (mappedIds.NamesAreFresh(maxAge))
        {
            namesText = "scene names kept (read recently)";
        }
        else
        {
            var names = await AttemptAsync(pass, () => StoreNamesAsync(ct), ct);
            namesText = names.Ok ? $"{names.Value} scene name(s)" : "scene names unavailable";
        }

        var rows = await db.Series.AsNoTracking()
            .Where(s => s.TvdbId != null)
            .Select(s => new SeriesRow(s.Id, s.Title, s.TvdbId!.Value, s.UseSceneNumbering, s.SceneNumberingRefreshedAt))
            .ToListAsync(ct);

        HashSet<int>? mapped = null;
        if (!pass.Cut)
        {
            var list = await AttemptAsync(pass, () => mappedIds.GetAsync(client, logger, maxAge, ct, throwOnFailure: true), ct);
            mapped = list.Value;
            if (!pass.Cut && mapped is null)
            {
                pass.Stop("the list of mapped series is unavailable", unreachable: true);
            }
        }

        var now = time.GetUtcNow().UtcDateTime;
        var candidates = mapped is null ? [] : rows.Where(r => r.UseSceneNumbering || mapped.Contains(r.TvdbId)).ToList();
        var noMap = mapped is null ? 0 : rows.Count - candidates.Count;
        var due = candidates
            .Where(r => r.RefreshedAt is not { } at || now - at >= maxAge)
            .OrderBy(r => r.RefreshedAt is null ? 0 : 1)
            .ThenBy(r => r.RefreshedAt)
            .ThenBy(r => r.Id)
            .ToList();

        var fetched = 0;
        var failed = 0;
        var next = 0;
        for (; next < due.Count && !pass.Cut; next++)
        {
            ct.ThrowIfCancellationRequested();
            var row = due[next];
            var attempt = await AttemptAsync(pass, () => client.GetMappingsAsync(row.TvdbId, ct), ct);
            if (pass.Cut)
            {
                break;
            }

            if (!attempt.Ok)
            {
                failed++;
                continue;
            }

            var series = await db.Series.FirstAsync(s => s.Id == row.Id, ct);
            var episodes = await db.Episodes.Where(e => e.SeriesId == row.Id).ToListAsync(ct);
            Apply(series, episodes, attempt.Value!);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            fetched++;
        }

        var withScene = await db.Series.CountAsync(s => s.UseSceneNumbering, ct);
        var left = due.Count - next;
        var summary = $"{namesText}; {rows.Count} series with a TVDB id: {fetched} mapping(s) fetched, {candidates.Count - due.Count} up to date, {noMap} without a mapping (not asked), {failed} failed, {pass.RateLimited} rate-limited answer(s); {withScene} with scene numbering";
        if (pass.Cut)
        {
            var resume = next < due.Count
                ? $"{left} series left, the next pass resumes with {due[next].Title} (tvdb {due[next].TvdbId})"
                : mapped is null ? "no series was read, the next pass starts again" : "nothing was left to read";
            summary += $"; stopped, {pass.CutReason}: {resume}";
            mappedIds.Failed($"{(pass.CutByRateLimit ? "TheXEM is rate-limiting Bigarr (HTTP 429/503)" : "TheXEM could not be read")}: the last pass stopped ({pass.CutReason}), {resume}.");
            logger.LogWarning("TheXEM pass: {Summary}", summary);
        }
        else
        {
            mappedIds.Answered();
            logger.LogInformation("TheXEM pass: {Summary}", summary);
        }

        return summary;
    }

    /// <summary>
    /// Runs one request of the pass. A rate-limit answer pauses the pass for its Retry-After and asks again (the same
    /// request); after <see cref="XemOptions.MaxConsecutiveFailures"/> in a row, or a Retry-After longer than
    /// <see cref="XemOptions.MaxPassPauseSeconds"/>, the pass is cut. Any other failure skips the request and counts too.
    /// </summary>
    private async Task<(bool Ok, T? Value)> AttemptAsync<T>(PassState pass, Func<Task<T>> request, CancellationToken ct)
    {
        var limit = Math.Max(1, options.Value.MaxConsecutiveFailures);
        var longest = TimeSpan.FromSeconds(Math.Max(1, options.Value.MaxPassPauseSeconds));
        while (true)
        {
            try
            {
                var value = await request();
                pass.InARow = 0;
                return (true, value);
            }
            catch (XemRateLimitedException ex)
            {
                pass.RateLimited++;
                pass.InARow++;
                if (pass.InARow >= limit)
                {
                    pass.Stop($"TheXEM refused {pass.InARow} requests in a row", unreachable: false);
                    return (false, default);
                }

                if (ex.RetryAfter > longest)
                {
                    pass.Stop($"TheXEM asked to wait {ex.RetryAfter.TotalMinutes:0} min", unreachable: false);
                    return (false, default);
                }

                logger.LogDebug("TheXEM asked to wait {Seconds:0} s, the pass pauses", ex.RetryAfter.TotalSeconds);
                await Task.Delay(ex.RetryAfter + TimeSpan.FromMilliseconds(100), time, ct);
            }
            catch (Exception ex) when (IsUnreachable(ex, ct))
            {
                pass.InARow++;
                logger.LogDebug("TheXEM request failed: {Message}", ex.Message);
                if (pass.InARow >= limit)
                {
                    pass.Stop($"{pass.InARow} requests failed in a row ({ex.Message})", unreachable: true);
                }

                return (false, default);
            }
        }
    }

    private void Apply(Domain.Tv.Series series, IReadOnlyCollection<Episode> episodes, IReadOnlyList<XemMapping> mappings)
    {
        if (mappings.Count > 0 || series.UseSceneNumbering)
        {
            series.UseSceneNumbering = XemNumbering.Apply(episodes, mappings);
        }

        series.SceneNumberingRefreshedAt = time.GetUtcNow().UtcDateTime;
    }

    internal static bool IsUnreachable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
        || (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested);

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;

    private sealed record SeriesRow(int Id, string Title, int TvdbId, bool UseSceneNumbering, DateTime? RefreshedAt);

    private sealed class PassState
    {
        public int InARow { get; set; }

        public int RateLimited { get; set; }

        public bool Cut { get; private set; }

        public bool CutByRateLimit { get; private set; }

        public string CutReason { get; private set; } = string.Empty;

        public void Stop(string reason, bool unreachable)
        {
            Cut = true;
            CutByRateLimit = !unreachable;
            CutReason = reason;
        }
    }
}
