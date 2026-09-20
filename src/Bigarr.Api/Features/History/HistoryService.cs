using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.History;

/// <summary>What to read from the journal. Every filter is optional; <paramref name="MovieIds"/> and <paramref name="MovieId"/> combine as an OR.</summary>
public sealed record HistoryFilter(
    int? MovieId = null,
    IReadOnlyCollection<int>? MovieIds = null,
    int? VersionId = null,
    IReadOnlyCollection<HistoryEventType>? EventTypes = null);

public sealed record HistoryPage(List<HistoryEvent> Events, int Total);

/// <summary>Read side of the journal, shared by the v1 endpoint and the compat layer. Events are written by the slices that do the work.</summary>
public sealed class HistoryService(BigarrDbContext db)
{
    public async Task<List<HistoryEvent>> ListAsync(HistoryFilter filter, int take, CancellationToken ct) =>
        await Query(filter).OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Take(take).ToListAsync(ct);

    public async Task<HistoryPage> PageAsync(HistoryFilter filter, int page, int pageSize, bool ascending, CancellationToken ct)
    {
        var query = Query(filter);
        var total = await query.CountAsync(ct);
        var ordered = ascending
            ? query.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            : query.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id);
        var events = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new HistoryPage(events, total);
    }

    private IQueryable<HistoryEvent> Query(HistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.History.AsNoTracking();
        var movieIds = (filter.MovieIds ?? []).ToList();
        if (filter.MovieId is { } movieId)
        {
            movieIds.Add(movieId);
        }

        if (movieIds.Count == 1)
        {
            var only = movieIds[0];
            query = query.Where(h => h.MovieId == only);
        }
        else if (movieIds.Count > 1)
        {
            query = query.Where(h => h.MovieId != null && movieIds.Contains(h.MovieId.Value));
        }

        if (filter.VersionId is { } versionId)
        {
            query = query.Where(h => h.TargetId == versionId);
        }

        if (filter.EventTypes is { Count: > 0 } types)
        {
            var list = types.ToList();
            query = list.Count == 1 ? query.Where(h => h.EventType == list[0]) : query.Where(h => list.Contains(h.EventType));
        }

        return query;
    }
}
