using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Movies;

/// <summary>
/// Radarr's "Refresh Movie" task: once a day, asks the provider again about the films <see cref="MetadataRefreshRule"/>
/// says are due, never refreshed first, then the oldest. Paced and saved film by film, so a restart or a cancel loses
/// nothing: what was refreshed is no longer due. A film that fails stays due for the next run; a streak of failures
/// stops the run (the task fails, the health check says so) instead of hammering a dead API.
/// </summary>
public sealed class MetadataRefreshJob(BigarrDbContext db, MovieService movies, IMetadataProvider metadata, IOptions<MetadataRefreshOptions> options, TimeProvider time, ILogger<MetadataRefreshJob> logger) : IScheduledJob
{
    public const string JobName = "metadataRefresh";

    /// <summary>A first run after a Radarr import takes about twenty minutes: say where it is now and then.</summary>
    private const int ProgressEvery = 500;

    public string Name => JobName;

    public string DisplayName => "Refresh metadata";

    public TimeSpan Interval => options.Value.RefreshInterval;

    /// <summary>Never on its own with the fixture catalogue: it knows a dozen films and would flag every other one deleted.</summary>
    public bool Enabled => options.Value.RefreshEnabled && !OnFixtures;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(Math.Max(0, options.Value.RefreshInitialDelayMinutes));

    /// <summary>A pass over the whole library: its own lane.</summary>
    public bool LongRunning => true;

    private bool OnFixtures => metadata.Name == "fixtures";

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        if (OnFixtures)
        {
            return "No TMDB API key: films are not refreshed against the fixture catalogue";
        }

        var now = time.GetUtcNow().UtcDateTime;
        var films = await db.Movies.AsNoTracking()
            .Select(m => new MetadataRefreshCandidate(m.Id, m.LastMetadataRefreshAt, m.TmdbStatus, m.InCinemasDate, m.DigitalReleaseDate, m.PhysicalReleaseDate))
            .ToListAsync(ct);
        var due = films
            .Where(f => MetadataRefreshRule.IsDue(f, now))
            .OrderBy(f => f.LastRefreshAt ?? DateTime.MinValue)
            .ThenBy(f => f.MovieId)
            .ToList();
        if (due.Count == 0)
        {
            return $"Nothing due among {films.Count} film(s)";
        }

        logger.LogInformation("Metadata refresh: {Due} of {Total} film(s) due", due.Count, films.Count);
        var spacing = options.Value.RefreshSpacing;
        var maxFailures = Math.Max(1, options.Value.RefreshMaxConsecutiveFailures);
        var nextCallAt = DateTimeOffset.MinValue;
        int refreshed = 0, failed = 0, streak = 0;
        foreach (var (index, film) in due.Index())
        {
            if (index > 0 && index % ProgressEvery == 0)
            {
                logger.LogInformation("Metadata refresh: {Done} of {Due} film(s) done, {Failed} failed", index, due.Count, failed);
            }

            var wait = nextCallAt - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, time, ct);
            }

            nextCallAt = time.GetUtcNow() + spacing;

            db.ChangeTracker.Clear();
            var movie = await db.Movies.FirstOrDefaultAsync(m => m.Id == film.MovieId, ct);
            if (movie is null)
            {
                continue; // deleted since the list was read
            }

            try
            {
                await movies.ApplyLatestMetadataAsync(movie, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed++;
                streak++;
                logger.LogWarning("Metadata refresh of {Title} (TMDB {TmdbId}) failed: {Error}", movie.Title, movie.TmdbId, ex.Message);
                if (streak >= maxFailures)
                {
                    throw new MetadataRefreshStoppedException(
                        $"Stopped after {streak} metadata provider failures in a row ({ex.Message.TrimEnd('.')}); {refreshed} of {due.Count} due film(s) were refreshed, the others wait for the next run.",
                        ex);
                }

                continue;
            }

            streak = 0;
            await db.SaveChangesAsync(ct);
            refreshed++;
        }

        return $"{due.Count} of {films.Count} film(s) due, {refreshed} refreshed, {failed} failed";
    }
}

/// <summary>A streak of provider failures ended the run: the task fails with this message and the health check repeats it.</summary>
public sealed class MetadataRefreshStoppedException(string message, Exception? inner = null) : Exception(message, inner);
