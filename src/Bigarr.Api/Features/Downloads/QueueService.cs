using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Downloads;

/// <summary>What the user does on a queue row: drop it, optionally with the torrent, optionally blocklisting the release.</summary>
public sealed class QueueService(BigarrDbContext db, DownloadClientResolver resolver, QueueState queue, TimeProvider time, ILogger<QueueService> logger)
{
    public async Task<Result<bool>> RemoveGrabAsync(int grabId, bool removeFromClient, bool deleteFiles, bool blocklist, CancellationToken ct)
    {
        var grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
        if (grab is null)
        {
            return Failure.NotFound("Queue item");
        }

        if (removeFromClient && grab.DownloadClientId is not null && grab.DownloadId is not null)
        {
            var client = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == grab.DownloadClientId, ct);
            if (client is not null)
            {
                try
                {
                    await resolver.For(client).RemoveAsync(client, grab.DownloadId, deleteFiles, ct);
                }
                catch (DownloadClientException ex)
                {
                    return new Failure(StatusCodes.Status502BadGateway, "Could not remove the download from the client", ex.Message);
                }
            }
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!grab.State.IsTerminal())
        {
            grab.TransitionTo(GrabState.Ignored);
        }

        if (blocklist)
        {
            await BlocklistAsync(grab, now, ct);
        }

        queue.Forget(grab.Id);
        await db.SaveChangesAsync(ct);
        queue.RequestRefresh();
        logger.LogInformation("{Title} removed from the queue (client: {Client}, files: {Files}, blocklist: {Blocklist})", grab.ReleaseTitle, removeFromClient, deleteFiles, blocklist);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<bool>> RemoveUntrackedAsync(int clientId, string downloadId, bool deleteFiles, CancellationToken ct)
    {
        var client = await db.DownloadClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (client is null)
        {
            return Failure.NotFound("Download client");
        }

        try
        {
            await resolver.For(client).RemoveAsync(client, downloadId, deleteFiles, ct);
        }
        catch (DownloadClientException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Could not remove the download from the client", ex.Message);
        }

        queue.RequestRefresh();
        return Result<bool>.Ok(true);
    }

    /// <summary>Blocklist on the managed version; a one-shot feeds the default version's list (blueprint §3.5).</summary>
    private async Task BlocklistAsync(Grab grab, DateTime now, CancellationToken ct)
    {
        var targetIds = grab.Targets.Select(t => t.TargetId).ToList();
        var versions = await db.MovieVersions.AsNoTracking().Include(v => v.Movie).Where(v => targetIds.Contains(v.Id)).ToListAsync(ct);
        var movieIds = versions.Select(v => v.MovieId).Distinct().ToList();
        var defaults = await db.MovieVersions.AsNoTracking().Where(v => movieIds.Contains(v.MovieId) && v.IsDefault).ToDictionaryAsync(v => v.MovieId, ct);
        foreach (var version in versions)
        {
            var owner = version.Mode == VersionMode.OneShot ? defaults.GetValueOrDefault(version.MovieId) ?? version : version;
            var exists = await db.Blocklist.AnyAsync(b => b.TargetId == owner.Id
                && ((grab.ReleaseGuid != null && b.ReleaseGuid == grab.ReleaseGuid) || (grab.ReleaseGuid == null && b.SourceTitle == grab.ReleaseTitle)), ct);
            if (exists)
            {
                continue;
            }

            db.Blocklist.Add(new BlocklistEntry
            {
                TargetId = owner.Id,
                ReleaseGuid = grab.ReleaseGuid,
                InfoHash = grab.InfoHash,
                SourceTitle = grab.ReleaseTitle,
                IndexerId = grab.IndexerId,
                IndexerName = grab.IndexerName,
                Reason = "Removed from the queue by the user",
                CreatedAt = now,
            });
            db.History.Add(new HistoryEvent
            {
                TargetId = owner.Id,
                MovieId = version.MovieId,
                TmdbId = version.Movie.TmdbId,
                MovieTitle = version.Movie.Title,
                VersionName = owner.Name,
                EventType = HistoryEventType.Blocklisted,
                SourceTitle = grab.ReleaseTitle,
                IndexerId = grab.IndexerId,
                IndexerName = grab.IndexerName,
                Data = new Dictionary<string, string> { ["reason"] = "Removed from the queue by the user", ["releaseGuid"] = grab.ReleaseGuid ?? string.Empty },
                CreatedAt = now,
            });
        }
    }
}
