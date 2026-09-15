using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Import;

/// <summary>Fills media info on files that were scanned or imported without it (blueprint §3.4) and empties the recycle bin.</summary>
public sealed class MediaMaintenance(BigarrDbContext db, MediaManagementService settings, IMediaInfoReader probe, RecycleBin recycleBin, IOptions<MediaOptions> options, ILogger<MediaMaintenance> logger)
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
}

/// <summary>Runs the maintenance on a timer: a backfill batch and the recycle bin retention, every hour by default.</summary>
public sealed class MediaMaintenanceRunner(IServiceScopeFactory scopeFactory, IOptions<MediaOptions> options, TimeProvider time, ILogger<MediaMaintenanceRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.BackfillEnabled)
        {
            logger.LogInformation("Media maintenance disabled (Bigarr:Media:BackfillEnabled = false)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.BackfillInitialDelaySeconds)), time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var maintenance = scope.ServiceProvider.GetRequiredService<MediaMaintenance>();
                    await maintenance.BackfillAsync(null, stoppingToken);
                    await maintenance.EmptyRecycleBinAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Media maintenance pass crashed");
                }

                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, settings.BackfillIntervalMinutes)), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
