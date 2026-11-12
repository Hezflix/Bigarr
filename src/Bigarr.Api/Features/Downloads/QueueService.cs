using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Downloads;

/// <summary>What the user does on a queue row: drop it, optionally with the torrent, optionally blocklisting the release.</summary>
public sealed class QueueService(BigarrDbContext db, DownloadClientResolver resolver, QueueState queue, BlocklistService blocklistService, TimeProvider time, ILogger<QueueService> logger)
{
    /// <remarks>
    /// Runs under the poll lock, like the poll and the import worker's claim: those are the other writers of a grab's
    /// state, each on its own copy of the row, and a removal slipping between their read and their save would have its
    /// <see cref="GrabState.Ignored"/> overwritten (failed and searched again, counted removed, or imported anyway).
    /// </remarks>
    public async Task<Result<bool>> RemoveGrabAsync(int grabId, bool removeFromClient, bool deleteFiles, bool blocklist, CancellationToken ct)
    {
        await queue.PollLock.WaitAsync(ct);
        try
        {
            return await RemoveGrabCoreAsync(grabId, removeFromClient, deleteFiles, blocklist, ct);
        }
        finally
        {
            queue.PollLock.Release();
        }
    }

    private async Task<Result<bool>> RemoveGrabCoreAsync(int grabId, bool removeFromClient, bool deleteFiles, bool blocklist, CancellationToken ct)
    {
        var grab = await db.Grabs.Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct);
        if (grab is null)
        {
            return Failure.NotFound("Queue item");
        }

        if (grab.State == GrabState.Importing)
        {
            // The import worker owns the grab until it is done: its files are being moved right now.
            return Failure.Conflict("Being imported", "This download is being imported. Wait for the import to finish, then remove it.");
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
            await blocklistService.BlockAsync(grab, "Removed from the queue by the user", now, ct);
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
}
