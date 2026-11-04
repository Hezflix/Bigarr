using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>
/// Drains the outbox after commit (blueprint §3.9): every undispatched row goes to the SSE stream and becomes one
/// <see cref="WebhookDelivery"/> per subscribed endpoint, then is stamped dispatched. Woken by the save interceptor,
/// with a slow sweep as a safety net.
/// </summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, OutboxSignal signal, WebhookSignal webhooks, EventStream stream, TimeProvider time, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public static readonly TimeSpan Sweep = TimeSpan.FromSeconds(30);

    public const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (await DispatchBatchAsync(stoppingToken) == BatchSize)
                    {
                        // keep going while the table still has undispatched rows
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Outbox dispatch crashed");
                }

                await signal.WaitAsync(Sweep, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>Also called by the tests through the scoped <see cref="Dispatch"/> helper.</summary>
    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        var events = await db.Outbox.Where(e => e.DispatchedAt == null).OrderBy(e => e.Id).Take(BatchSize).ToListAsync(ct);
        if (events.Count == 0)
        {
            return 0;
        }

        var endpoints = await db.NotificationEndpoints.AsNoTracking().Include(e => e.Tags).Where(e => e.Enabled).ToListAsync(ct);
        var parsed = endpoints.Count == 0 ? [] : events.ToDictionary(e => e.Id, e => EventJson.Deserialize(e.Type, e.Payload));
        var tags = await LiveTagsAsync(db, endpoints, parsed.Values, ct);
        IEnumerable<int> TagsOf(VersionRef version) => version.Tags is { Count: > 0 } snapshot ? snapshot.Select(t => t.Id) : tags.GetValueOrDefault(version.Id) ?? [];

        var deliveries = 0;
        foreach (var outboxEvent in events)
        {
            foreach (var endpoint in endpoints)
            {
                foreach (var versionId in WebhookRouting.Deliveries(endpoint, outboxEvent.Type, parsed.GetValueOrDefault(outboxEvent.Id), TagsOf))
                {
                    db.WebhookDeliveries.Add(new WebhookDelivery
                    {
                        EventId = outboxEvent.Id,
                        EndpointId = endpoint.Id,
                        EventType = outboxEvent.Type,
                        VersionId = versionId,
                        NextAttemptAt = now,
                        CreatedAt = now,
                    });
                    deliveries++;
                }
            }

            outboxEvent.DispatchedAt = now;
        }

        await db.SaveChangesAsync(ct);

        // Published after the commit: a listener never learns about an event that could still roll back.
        foreach (var outboxEvent in events)
        {
            stream.Publish(new ServerEvent(outboxEvent.Id, outboxEvent.Type, outboxEvent.Payload, outboxEvent.CreatedAt));
        }

        if (deliveries > 0)
        {
            webhooks.Notify();
        }

        logger.LogDebug("Dispatched {Events} event(s) into {Deliveries} webhook deliver(ies)", events.Count, deliveries);
        return events.Count;
    }

    /// <summary>
    /// The snapshot's tags decide; a version whose snapshot has none may just not have had them loaded by the slice
    /// that published, so its current tags are read, once per batch and only when an endpoint filters on tags.
    /// </summary>
    private static async Task<Dictionary<int, List<int>>> LiveTagsAsync(BigarrDbContext db, List<NotificationEndpoint> endpoints, IEnumerable<DomainEvent?> events, CancellationToken ct)
    {
        if (!endpoints.Any(e => e.Tags.Count > 0))
        {
            return [];
        }

        var ids = events.OfType<DomainEvent>().SelectMany(WebhookRouting.Subjects).Where(v => v.Tags is not { Count: > 0 }).Select(v => v.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.MovieVersions.AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, Tags = v.Tags.Select(t => t.Id).ToList() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Tags);
    }
}
