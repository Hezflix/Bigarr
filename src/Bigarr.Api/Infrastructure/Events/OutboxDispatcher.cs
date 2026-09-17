using Bigarr.Api.Infrastructure.Persistence;
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

        var endpoints = await db.NotificationEndpoints.AsNoTracking().Where(e => e.Enabled).ToListAsync(ct);
        var deliveries = 0;
        foreach (var outboxEvent in events)
        {
            foreach (var endpoint in endpoints.Where(e => e.Subscribes(outboxEvent.Type)))
            {
                db.WebhookDeliveries.Add(new WebhookDelivery
                {
                    EventId = outboxEvent.Id,
                    EndpointId = endpoint.Id,
                    EventType = outboxEvent.Type,
                    NextAttemptAt = now,
                    CreatedAt = now,
                });
                deliveries++;
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
}
