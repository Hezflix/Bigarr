using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Import;

/// <summary>
/// Fills media info on files that were scanned or imported without it, replaces the one Radarr/Sonarr gave at import with
/// Bigarr's own reading, and empties the recycle bin. A file that could not be read is recorded as tried and comes back
/// after <see cref="MediaOptions.BackfillRetryHours"/>, behind the files never tried; whatever media info it had stays.
/// </summary>
public sealed class MediaMaintenance(BigarrDbContext db, MediaManagementService settings, IMediaInfoReader probe, RecycleBin recycleBin, IOptions<MediaOptions> options, IOptions<NotificationsOptions> notifications, TimeProvider time, ILogger<MediaMaintenance> logger)
{
    public async Task<BackfillResponse> BackfillAsync(int? batchSize, CancellationToken ct)
    {
        // Pending: no media info yet, or only the one Radarr/Sonarr gave at import, which Bigarr's own probe replaces.
        var remaining = await db.MediaFiles.CountAsync(f => f.MediaInfo == null || f.MediaInfoFromArr, ct)
                        + await db.EpisodeFiles.CountAsync(f => f.MediaInfo == null || f.MediaInfoFromArr, ct);
        if (!probe.IsAvailable || remaining == 0)
        {
            return new BackfillResponse(probe.IsAvailable, 0, remaining);
        }

        var take = Math.Clamp(batchSize ?? options.Value.BackfillBatchSize, 1, 500);
        var now = time.GetUtcNow().UtcDateTime;
        var retryBefore = now.AddHours(-Math.Max(1, options.Value.BackfillRetryHours));

        // In each kind: files with nothing at all first, then the *arr's to replace; the ones never tried before the failed.
        var films = await db.MediaFiles
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Where(f => (f.MediaInfo == null || f.MediaInfoFromArr) && (f.MediaInfoTriedAt == null || f.MediaInfoTriedAt < retryBefore))
            .OrderBy(f => f.MediaInfo != null).ThenBy(f => f.MediaInfoTriedAt != null).ThenBy(f => f.MediaInfoTriedAt).ThenBy(f => f.Id)
            .Take(take)
            .ToListAsync(ct);
        var episodes = await db.EpisodeFiles
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Where(f => (f.MediaInfo == null || f.MediaInfoFromArr) && (f.MediaInfoTriedAt == null || f.MediaInfoTriedAt < retryBefore))
            .OrderBy(f => f.MediaInfo != null).ThenBy(f => f.MediaInfoTriedAt != null).ThenBy(f => f.MediaInfoTriedAt).ThenBy(f => f.Id)
            .Take(take)
            .ToListAsync(ct);

        // The batch is shared fairly: half each, and what one kind does not use goes to the other.
        var filmShare = Math.Min(films.Count, Math.Max(take - (take / 2), take - episodes.Count));
        var episodeShare = Math.Min(episodes.Count, take - filmShare);

        var probed = 0;
        foreach (var file in films.Take(filmShare))
        {
            ct.ThrowIfCancellationRequested();
            var info = await ProbeAsync(Path.Combine(file.Version.RootFolder.Path, file.Version.FolderName, file.RelativePath), file.OriginalReleaseTitle, ct);
            if (info is null)
            {
                // Nothing read: the *arr's media info, if any, stays.
                file.MediaInfoTriedAt = now;
                continue;
            }

            file.MediaInfo = info;
            file.MediaInfoFromArr = false;
            file.MediaInfoTriedAt = null;
            probed++;
        }

        foreach (var file in episodes.Take(episodeShare))
        {
            ct.ThrowIfCancellationRequested();
            var info = await ProbeAsync(Path.Combine(file.Version.RootFolder.Path, file.Version.FolderName, file.RelativePath), file.OriginalReleaseTitle, ct);
            if (info is null)
            {
                // Nothing read: the *arr's media info, if any, stays.
                file.MediaInfoTriedAt = now;
                continue;
            }

            file.MediaInfo = info;
            file.MediaInfoFromArr = false;
            file.MediaInfoTriedAt = null;
            probed++;
        }

        // A failed attempt is saved too: that is what moves the file behind the ones never tried.
        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        if (probed > 0)
        {
            logger.LogInformation("Media info backfill: {Count} file(s) probed, {Remaining} left", probed, remaining - probed);
        }

        return new BackfillResponse(true, probed, remaining - probed);
    }

    private async Task<Domain.Movies.MediaInfo?> ProbeAsync(string path, string? releaseTitle, CancellationToken ct) =>
        File.Exists(path) ? await probe.ReadAsync(path, releaseTitle, ct) : null;

    public async Task<int> EmptyRecycleBinAsync(CancellationToken ct)
    {
        var config = await settings.GetAsync(ct);
        return string.IsNullOrWhiteSpace(config.RecycleBinPath) ? 0 : recycleBin.Cleanup(config.RecycleBinPath, config.RecycleBinRetentionDays);
    }

    /// <summary>Dispatched events past their retention go, and their deliveries with them (cascade).</summary>
    public async Task<int> PurgeOutboxAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, notifications.Value.RetentionDays));
        return await db.Outbox.Where(e => e.DispatchedAt != null && e.DispatchedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>The maintenance as a scheduled task: a backfill batch, the recycle bin retention and the outbox purge, every hour by default.</summary>
public sealed class MediaMaintenanceJob(MediaMaintenance maintenance, IOptions<MediaOptions> options) : IScheduledJob
{
    public const string JobName = "mediaMaintenance";

    public string Name => JobName;

    public string DisplayName => "Media maintenance";

    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, options.Value.BackfillIntervalMinutes));

    public bool Enabled => options.Value.BackfillEnabled;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(Math.Max(0, options.Value.BackfillInitialDelaySeconds));

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var backfill = await maintenance.BackfillAsync(null, ct);
        var recycled = await maintenance.EmptyRecycleBinAsync(ct);
        var purged = await maintenance.PurgeOutboxAsync(ct);
        return $"{backfill.Probed} file(s) probed ({backfill.Remaining} left), {recycled} recycled file(s) removed, {purged} old event(s) purged";
    }
}
