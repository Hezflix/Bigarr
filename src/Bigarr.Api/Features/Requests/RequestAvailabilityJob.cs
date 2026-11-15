using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// Sends "available" once per request: the state itself is read from the library on every read, this only notices when an
/// approved request first becomes fully available and writes the event to the outbox (marking the request so it goes out
/// once). Reads only the library, so it runs with the scheduler off too.
/// </summary>
public sealed class RequestAvailabilityJob(BigarrDbContext db, RequestStates states, OutboxWriter outbox, TimeProvider time) : IScheduledJob
{
    public const string JobName = "requestAvailability";

    public string Name => JobName;

    public string DisplayName => "Request availability";

    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(1);

    public bool RunsWhenSchedulerDisabled => true;

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var waiting = await db.MediaRequests.Include(r => r.RequestedBy)
            .Where(r => r.Status == RequestStatus.Approved && r.AvailableAt == null)
            .ToListAsync(ct);
        if (waiting.Count == 0)
        {
            return null;
        }

        var shown = await states.ResolveAsync(waiting, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var available = waiting.Where(r => shown[r.Id] == RequestState.Available).ToList();
        foreach (var request in available)
        {
            request.AvailableAt = now;
            outbox.Publish(new RequestAvailable(RequestRef.From(request, request.RequestedBy.Username)));
        }

        await db.SaveChangesAsync(ct);
        return available.Count == 0 ? null : $"{available.Count} request(s) now available";
    }
}
