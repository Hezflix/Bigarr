using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>The list of TVDB ids XEM has a mapping for, kept 3 hours like Sonarr's cache. Singleton.</summary>
public sealed class XemMappedIds(TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<int>? _ids;
    private DateTimeOffset _fetchedAt;

    /// <summary>The last time TheXEM could not be read, and why; cleared by the next answer. Read by the health check.</summary>
    public (DateTimeOffset At, string Message)? LastFailure { get; private set; }

    public void Failed(string message) => LastFailure = (time.GetUtcNow(), message);

    public void Answered() => LastFailure = null;

    /// <summary>Null when the list could not be fetched yet: callers then ask for the series' mapping directly.</summary>
    public async Task<HashSet<int>?> GetAsync(IXemClient client, ILogger logger, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_ids is not null && time.GetUtcNow() - _fetchedAt < Lifetime)
            {
                return _ids;
            }

            try
            {
                _ids = [.. await client.GetMappedTvdbIdsAsync(ct)];
                _fetchedAt = time.GetUtcNow();
                Answered();
            }
            catch (Exception ex) when (XemService.IsUnreachable(ex, ct))
            {
                Failed(ex.Message);
                logger.LogWarning("TheXEM series list unavailable: {Message}", ex.Message);
            }

            return _ids;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Keeps XEM data in the database: scene numbers on episodes (refreshed with the series metadata and on the slow
/// schedule) and scene names (global, slow schedule). TheXEM being down never fails a caller: the last known data stays.
/// </summary>
public sealed class XemService(BigarrDbContext db, IXemClient client, XemMappedIds mappedIds, IOptions<XemOptions> options, TimeProvider time, ILogger<XemService> logger)
{
    public bool Enabled => options.Value.Enabled;

    /// <summary>The slow schedule's cadence (<see cref="XemOptions.RefreshIntervalHours"/>).</summary>
    public TimeSpan RefreshInterval => TimeSpan.FromHours(Math.Max(1, options.Value.RefreshIntervalHours));

    /// <summary>
    /// Fetches the series' mapping and writes scene numbers onto <paramref name="episodes"/> (tracked or new; the caller
    /// saves). Returns false when nothing could be fetched. Scene numbers map onto TVDB's numbers, which every episode
    /// keeps whatever order the series shows (blueprint §8): kept current for every series.
    /// </summary>
    /// <param name="maxAge">
    /// From a metadata refresh that brought no new episode: a mapping read more recently than this is kept (the slow
    /// schedule reads every mapped show anyway), and when TheXEM's list of mapped shows cannot be read, no show is asked
    /// one by one. Null asks whenever the show is on TheXEM's list (adding a show, new episodes, the slow schedule).
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
            var mapped = await mappedIds.GetAsync(client, logger, ct);
            if (mapped is null && maxAge is not null)
            {
                return false;
            }
            IReadOnlyList<XemMapping> mappings = mapped is not null && !mapped.Contains(tvdbId) && !series.UseSceneNumbering
                ? []
                : await client.GetMappingsAsync(tvdbId, ct);

            if (mappings.Count == 0 && !series.UseSceneNumbering)
            {
                series.SceneNumberingRefreshedAt = time.GetUtcNow().UtcDateTime;
                return true;
            }

            series.UseSceneNumbering = XemNumbering.Apply(episodes, mappings);
            series.SceneNumberingRefreshedAt = time.GetUtcNow().UtcDateTime;
            logger.LogDebug("XEM mapping for {Title}: {Count} row(s)", series.Title, mappings.Count);
            return true;
        }
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            mappedIds.Failed(ex.Message);
            logger.LogWarning("TheXEM unavailable for {Title} (tvdb {TvdbId}), scene numbers kept as they were: {Message}", series.Title, tvdbId, ex.Message);
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

        IReadOnlyList<XemName> names;
        try
        {
            names = await client.GetNamesAsync(ct);
            mappedIds.Answered();
        }
        catch (Exception ex) when (IsUnreachable(ex, ct))
        {
            mappedIds.Failed(ex.Message);
            logger.LogWarning("TheXEM scene names unavailable, the previous list stays: {Message}", ex.Message);
            return null;
        }

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

    /// <summary>The slow schedule: scene names, then the mapping of every series with a TVDB id.</summary>
    public async Task<string> RefreshAllAsync(CancellationToken ct)
    {
        if (!Enabled)
        {
            return "TheXEM is disabled";
        }

        var names = await RefreshNamesAsync(ct);
        var ids = await db.Series.Where(s => s.TvdbId != null).OrderBy(s => s.Id).Select(s => s.Id).ToListAsync(ct);
        int refreshed = 0, mapped = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var series = await db.Series.FirstAsync(s => s.Id == id, ct);
            var episodes = await db.Episodes.Where(e => e.SeriesId == id).ToListAsync(ct);
            if (await RefreshSeriesAsync(series, episodes, ct))
            {
                refreshed++;
                mapped += series.UseSceneNumbering ? 1 : 0;
                await db.SaveChangesAsync(ct);
            }

            db.ChangeTracker.Clear();
        }

        return $"{(names is null ? "scene names unavailable" : $"{names} scene name(s)")}, {refreshed}/{ids.Count} series checked, {mapped} with scene numbering";
    }

    internal static bool IsUnreachable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
        || (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested);

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;
}
