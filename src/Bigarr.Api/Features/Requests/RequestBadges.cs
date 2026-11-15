using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>A title's active requests per slot, and per season for a series.</summary>
public sealed record TitleRequests(RequestSlotsResponse Slots, IReadOnlyDictionary<int, RequestSlotsResponse> Seasons);

/// <summary>
/// What Discover shows of requests next to availability: for each title (and season), whether someone asked for it in
/// each slot and where that request stands (pending, approved, failed). Who asked is not said.
/// </summary>
public sealed class RequestBadges(BigarrDbContext db)
{
    public async Task<IReadOnlyDictionary<int, TitleRequests>> ForAsync(RequestMediaType type, IReadOnlyCollection<int> tmdbIds, CancellationToken ct)
    {
        if (tmdbIds.Count == 0)
        {
            return new Dictionary<int, TitleRequests>();
        }

        var active = await db.MediaRequests.AsNoTracking()
            .Where(r => r.MediaType == type && tmdbIds.Contains(r.TmdbId) && r.Status != RequestStatus.Declined)
            .Select(r => new { r.TmdbId, r.FourK, r.Status, r.Seasons })
            .ToListAsync(ct);

        return active.GroupBy(r => r.TmdbId).ToDictionary(g => g.Key, g =>
        {
            RequestState? Of(IEnumerable<RequestStatus> statuses) => statuses.Select(s => (RequestStatus?)s).OrderBy(Rank).FirstOrDefault() is { } best ? RequestStates.Stored(best) : null;
            var slots = new RequestSlotsResponse(Of(g.Where(r => !r.FourK).Select(r => r.Status)), Of(g.Where(r => r.FourK).Select(r => r.Status)));
            var seasons = g.SelectMany(r => r.Seasons.Select(n => (Season: n, r.FourK, r.Status)))
                .GroupBy(x => x.Season)
                .ToDictionary(s => s.Key, s => new RequestSlotsResponse(Of(s.Where(x => !x.FourK).Select(x => x.Status)), Of(s.Where(x => x.FourK).Select(x => x.Status))));
            return new TitleRequests(slots, seasons);
        });
    }

    /// <summary>The badge a title shows when several requests share a slot: approved before pending before failed.</summary>
    private static int Rank(RequestStatus? status) => status switch
    {
        RequestStatus.Approved => 0,
        RequestStatus.Pending => 1,
        _ => 2,
    };
}
