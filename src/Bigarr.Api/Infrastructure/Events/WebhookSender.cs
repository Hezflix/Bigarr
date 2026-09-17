using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>
/// Sends due <see cref="WebhookDelivery"/> rows and writes the outcome back on the row (blueprint §3.9). A failure
/// schedules the next attempt with an escalating delay; after <see cref="NotificationsOptions.MaxAttempts"/> the
/// delivery is given up until a manual retry. On startup every pending delivery is due at once: whatever was
/// waiting when the process stopped is replayed.
/// </summary>
public sealed class WebhookSender(IServiceScopeFactory scopeFactory, WebhookSignal signal, IOptions<NotificationsOptions> options, TimeProvider time, ILogger<WebhookSender> logger) : BackgroundService
{
    public const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Webhook sending disabled (Bigarr:Notifications:Enabled = false)");
            return;
        }

        try
        {
            await ReplayPendingAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (await SendDueAsync(stoppingToken) == BatchSize)
                    {
                        // more may be due
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Webhook sending crashed");
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, settings.SweepSeconds)), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>A restart makes every pending delivery due now: the wait was a wall-clock delay, and the process was down anyway.</summary>
    private async Task ReplayPendingAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var replayed = await db.WebhookDeliveries
            .Where(d => d.DeliveredAt == null && d.NextAttemptAt != null && d.NextAttemptAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, now), ct);
        if (replayed > 0)
        {
            logger.LogInformation("{Count} webhook deliver(ies) left pending by the previous run are replayed now", replayed);
        }
    }

    private async Task<int> SendDueAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<WebhookClient>();
        var settings = options.Value;
        var now = time.GetUtcNow().UtcDateTime;

        var due = await db.WebhookDeliveries
            .Include(d => d.Event)
            .Include(d => d.Endpoint)
            .Where(d => d.DeliveredAt == null && d.NextAttemptAt != null && d.NextAttemptAt <= now)
            .OrderBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var delivery in due)
        {
            ct.ThrowIfCancellationRequested();
            var attemptAt = time.GetUtcNow().UtcDateTime;
            delivery.Attempts++;
            delivery.LastAttemptAt = attemptAt;

            if (!delivery.Endpoint.Enabled)
            {
                delivery.NextAttemptAt = null;
                delivery.LastError = "Endpoint is disabled.";
                continue;
            }

            var result = await client.PostAsync(delivery.Endpoint, delivery.EventType, delivery.EventId, delivery.Event.Payload, delivery.Event.CreatedAt, delivery.Id.ToString(), ct);
            delivery.LastStatusCode = result.StatusCode;
            if (result.Success)
            {
                delivery.DeliveredAt = attemptAt;
                delivery.NextAttemptAt = null;
                delivery.LastError = null;
                logger.LogDebug("Webhook {Endpoint} accepted {EventType} #{EventId} in {Elapsed} ms", delivery.Endpoint.Name, delivery.EventType, delivery.EventId, (int)result.Elapsed.TotalMilliseconds);
                continue;
            }

            delivery.LastError = result.Error;
            if (delivery.Attempts >= Math.Max(1, settings.MaxAttempts))
            {
                delivery.NextAttemptAt = null;
                logger.LogWarning("Webhook {Endpoint} gave up on {EventType} #{EventId} after {Attempts} attempt(s): {Error}", delivery.Endpoint.Name, delivery.EventType, delivery.EventId, delivery.Attempts, result.Error);
            }
            else
            {
                delivery.NextAttemptAt = attemptAt + settings.RetryDelay(delivery.Attempts);
                logger.LogWarning("Webhook {Endpoint} failed on {EventType} #{EventId} (attempt {Attempts}): {Error}. Next try at {Next:u}", delivery.Endpoint.Name, delivery.EventType, delivery.EventId, delivery.Attempts, result.Error, delivery.NextAttemptAt);
            }
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return due.Count;
    }
}
