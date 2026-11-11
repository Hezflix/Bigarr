namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>What <see cref="SeriesRefreshRule"/> reads of a show.</summary>
public sealed record SeriesRefreshCandidate(int SeriesId, string SortTitle, DateTime? LastRefreshAt, SeriesStatus Status, DateTime? LastEpisodeAirDateUtc, bool AiredEpisodeWithoutTitle);

/// <summary>
/// Sonarr's <c>ShouldRefreshSeries</c>, word for word: a show not refreshed for 30 days is due; one with an aired
/// episode still titled TBA is due; one refreshed within 6 hours is not; a show that has not ended is due; an ended show
/// is due only while its last episode aired less than 30 days ago. A show never refreshed is always due.
/// </summary>
public static class SeriesRefreshRule
{
    public static bool IsDue(SeriesRefreshCandidate candidate, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.LastRefreshAt is not { } last || last < now.AddDays(-30))
        {
            return true;
        }

        if (candidate.AiredEpisodeWithoutTitle)
        {
            return true;
        }

        if (last >= now.AddHours(-6))
        {
            return false;
        }

        if (candidate.Status != SeriesStatus.Ended)
        {
            return true;
        }

        return candidate.LastEpisodeAirDateUtc is { } aired && aired > now.AddDays(-30);
    }
}

/// <summary>
/// Sonarr's "Refresh Series" task: every 12 hours, asks the providers again about the shows <see cref="SeriesRefreshRule"/>
/// says are due, in title order like Sonarr. A refresh brings new episodes and seasons (monitored per each version's
/// settings), recomputes the air times, refreshes TheXEM's mapping of the show and searches the new episodes that aired
/// recently (<see cref="SeriesService.RefreshAsync"/>). Saved show by show; a streak of provider failures stops the run.
/// With the fixture catalogue it never runs on its own, and by hand it skips the shows the catalogue does not know.
/// </summary>
public sealed class SeriesRefreshJob(BigarrDbContext db, SeriesService series, ISeriesMetadataProvider metadata, IOptions<MetadataRefreshOptions> options, TimeProvider time, ILogger<SeriesRefreshJob> logger) : IScheduledJob
{
    public const string JobName = "seriesRefresh";

    public string Name => JobName;

    public string DisplayName => "Refresh series";

    public TimeSpan Interval => options.Value.SeriesRefreshInterval;

    public bool Enabled => options.Value.RefreshEnabled && !OnFixtures;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(Math.Max(0, options.Value.RefreshInitialDelayMinutes));

    private bool OnFixtures => metadata.Name == "fixtures";

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var shows = await db.Series.AsNoTracking()
            .Select(s => new SeriesRefreshCandidate(
                s.Id,
                s.SortTitle,
                s.LastMetadataRefreshAt,
                s.Status,
                s.Episodes.Max(e => e.AirDateUtc),
                s.Episodes.Any(e => e.SeasonNumber > 0 && e.AirDateUtc != null && e.AirDateUtc < now && e.Title == Infrastructure.Metadata.SeriesMetadataProvider.UntitledEpisode)))
            .ToListAsync(ct);
        var due = shows.Where(s => SeriesRefreshRule.IsDue(s, now)).OrderBy(s => s.SortTitle, StringComparer.Ordinal).ToList();
        if (due.Count == 0)
        {
            return $"Nothing due among {shows.Count} series";
        }

        var spacing = options.Value.RefreshSpacing;
        var maxFailures = Math.Max(1, options.Value.RefreshMaxConsecutiveFailures);
        var nextCallAt = DateTimeOffset.MinValue;
        int refreshed = 0, failed = 0, skipped = 0, streak = 0;
        foreach (var show in due)
        {
            var wait = nextCallAt - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, ct);
            }

            nextCallAt = time.GetUtcNow() + spacing;
            db.ChangeTracker.Clear();
            if (OnFixtures && !await KnownToFixturesAsync(show.SeriesId, ct))
            {
                skipped++;
                continue;
            }

            var result = await series.RefreshAsync(show.SeriesId, ct);
            if (result.IsSuccess)
            {
                streak = 0;
                refreshed++;
                continue;
            }

            if (result.Failure!.StatusCode == StatusCodes.Status404NotFound)
            {
                continue; // deleted since the list was read
            }

            failed++;
            streak++;
            logger.LogWarning("Series refresh of {Series} failed: {Error}", show.SeriesId, result.Failure.Detail ?? result.Failure.Title);
            if (streak >= maxFailures)
            {
                throw new MetadataRefreshStoppedException(
                    $"Stopped after {streak} metadata provider failures in a row ({(result.Failure.Detail ?? result.Failure.Title).TrimEnd('.')}); {refreshed} of {due.Count} due series were refreshed, the others wait for the next run.");
            }
        }

        return $"{due.Count} of {shows.Count} series due, {refreshed} refreshed, {failed} failed" + (skipped > 0 ? $", {skipped} not in the fixture catalogue" : string.Empty);
    }

    private async Task<bool> KnownToFixturesAsync(int seriesId, CancellationToken ct)
    {
        var ids = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => new { s.TvdbId, s.TmdbId }).FirstAsync(ct);
        return (ids.TvdbId is { } tvdb && await metadata.GetByTvdbIdAsync(tvdb, withEpisodes: false, ct) is not null)
            || (ids.TmdbId is { } tmdb && await metadata.GetByTmdbIdAsync(tmdb, withEpisodes: false, ct) is not null);
    }
}
