using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// An approval is saved before the library add, so a process that stops in between (restart, kill, crash) leaves an
/// approved request with nothing in the library, and nothing retries it: only a failed request can be retried. At startup
/// no approval can be running, so such a request is put back to failed, where its Retry button works (the add is safe to
/// run again). A request imported from Seerr is history and never touched; one that has its library id was added.
/// </summary>
public sealed class StrandedApprovals(BigarrDbContext db, OutboxWriter outbox, TimeProvider time, ILogger<StrandedApprovals> logger) : IStartupTask
{
    public const string Reason = "The add was interrupted before the library took it (the app stopped). Retry it.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stranded = await db.MediaRequests
            .Include(r => r.RequestedBy)
            .Where(r => r.Status == RequestStatus.Approved && r.MovieId == null && r.SeriesId == null && r.SourceRef == null)
            .ToListAsync(cancellationToken);
        if (stranded.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var request in stranded)
        {
            request.Status = RequestStatus.Failed;
            request.Error = Reason;
            request.UpdatedAt = now;
            outbox.Publish(new RequestFailed(RequestRef.From(request, request.RequestedBy.Username), Reason, Forward: false));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("{Count} approved request(s) had nothing in the library after the last shutdown; they are failed, ready to retry", stranded.Count);
    }
}
