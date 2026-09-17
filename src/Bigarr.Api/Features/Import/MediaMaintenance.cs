using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Import;

/// <summary>Fills media info on files that were scanned or imported without it (blueprint §3.4) and empties the recycle bin.</summary>
public sealed class MediaMaintenance(BigarrDbContext db, MediaManagementService settings, IMediaInfoReader probe, RecycleBin recycleBin, IOptions<MediaOptions> options, IOptions<NotificationsOptions> notifications, TimeProvider time, ILogger<MediaMaintenance> logger)
{
    public async Task<BackfillResponse> BackfillAsync(int? batchSize, CancellationToken ct)
    {
        var remaining = await db.MediaFiles.CountAsync(f => f.MediaInfo == null, ct);
        if (!probe.IsAvailable || remaining == 0)
        {
            return new BackfillResponse(probe.IsAvailable, 0, remaining);
        }

        var take = Math.Clamp(batchSize ?? options.Value.BackfillBatchSize, 1, 500);
        var files = await db.MediaFiles
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Where(f => f.MediaInfo == null)
            .OrderBy(f => f.Id)
            .Take(take)
            .ToListAsync(ct);

        var probed = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(file.Version.RootFolder.Path, file.Version.FolderName, file.RelativePath);
            if (!File.Exists(path))
            {
                continue;
            }

            var info = await probe.ReadAsync(path, file.OriginalReleaseTitle, ct);
            if (info is null)
            {
                continue;
            }

            file.MediaInfo = info;
            probed++;
        }

        if (probed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Media info backfill: {Count} file(s) probed, {Remaining} left", probed, remaining - probed);
        }

        return new BackfillResponse(true, probed, remaining - probed);
    }

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
