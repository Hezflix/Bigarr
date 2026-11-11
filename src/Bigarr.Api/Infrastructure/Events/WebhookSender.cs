using Bigarr.Api.Features.Notifications.Discord;
using Bigarr.Api.Features.Notifications.Radarr;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>
/// Sends due <see cref="WebhookDelivery"/> rows and writes the outcome back on the row (blueprint §3.9). A failure
/// schedules the next attempt with an escalating delay; after <see cref="NotificationsOptions.MaxAttempts"/> the
/// delivery is given up until a manual retry. A 429 is no failure: the delivery, and every other one due for the same
/// endpoint, waits what the receiver asked (Discord's <c>Retry-After</c>), without spending an attempt. On startup
/// every pending delivery is due at once: whatever was waiting when the process stopped is replayed.
/// </summary>
public sealed class WebhookSender(IServiceScopeFactory scopeFactory, WebhookSignal signal, IOptions<NotificationsOptions> options, TimeProvider time, ILogger<WebhookSender> logger) : BackgroundService
{
    public const int BatchSize = 50;

    /// <summary>When the earliest rate limit of the last pass ends: the sender looks again then rather than at the next sweep.</summary>
    private DateTime? _rateLimitedUntil;

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

                await signal.WaitAsync(NextLook(settings), time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private TimeSpan NextLook(NotificationsOptions settings)
    {
        var sweep = TimeSpan.FromSeconds(Math.Max(1, settings.SweepSeconds));
        if (_rateLimitedUntil is not { } until)
        {
            return sweep;
        }

        _rateLimitedUntil = null;
        var left = until - time.GetUtcNow().UtcDateTime;
        return left < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left < sweep ? left : sweep;
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

        var rateLimited = new Dictionary<int, DateTime>();
        foreach (var delivery in due)
        {
            ct.ThrowIfCancellationRequested();
            if (rateLimited.TryGetValue(delivery.EndpointId, out var until))
            {
                // The endpoint asked to wait: sending the rest now would only be refused again.
                delivery.NextAttemptAt = until;
                continue;
            }

            var attemptAt = time.GetUtcNow().UtcDateTime;
            delivery.Attempts++;
            delivery.LastAttemptAt = attemptAt;

            if (!delivery.Endpoint.Enabled)
            {
                delivery.NextAttemptAt = null;
                delivery.LastError = "Endpoint is disabled.";
                continue;
            }

            var body = delivery.Endpoint.Format switch
            {
                WebhookFormat.Radarr => RadarrWebhook.Body(delivery.EventType, delivery.Event.Payload, delivery.VersionId, settings.ApplicationUrl),
                WebhookFormat.Discord => DiscordWebhook.Body(delivery.EventType, delivery.Event.Payload, delivery.Endpoint.Discord),
                WebhookFormat.Sonarr => Features.Notifications.Sonarr.SonarrWebhook.Body(delivery.EventType, delivery.Event.Payload, delivery.VersionId, settings.ApplicationUrl),
                _ => WebhookClient.Body(delivery.EventType, delivery.EventId, delivery.Event.Payload, delivery.Event.CreatedAt, settings.ApplicationUrl),
            };
            if (body is null)
            {
                // The endpoint switched format after the delivery was created, to one that has nothing for this event.
                delivery.NextAttemptAt = null;
                delivery.LastError = delivery.Endpoint.Format switch
                {
                    WebhookFormat.Discord => "The Discord format posts nothing for this event.",
                    WebhookFormat.Sonarr => "Sonarr's webhook format has nothing for this event.",
                    _ => "Radarr's webhook format has nothing for this event.",
                };
                continue;
            }

            var result = await client.SendAsync(delivery.Endpoint, delivery.EventType, body, delivery.Id.ToString(), ct);
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
            if (result.RetryAfter is { } wait)
            {
                // Rate limited: the attempt is given back, the delay is the receiver's, never longer than the escalation's longest.
                var longest = settings.RetryDelay(int.MaxValue);
                delivery.Attempts--;
                delivery.NextAttemptAt = attemptAt + (wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > longest ? longest : wait);
                rateLimited[delivery.EndpointId] = delivery.NextAttemptAt.Value;
                _rateLimitedUntil = _rateLimitedUntil is { } earliest && earliest < delivery.NextAttemptAt ? earliest : delivery.NextAttemptAt;
                logger.LogInformation("Webhook {Endpoint} is rate limited on {EventType} #{EventId}: {Error}. Next try at {Next:u}", delivery.Endpoint.Name, delivery.EventType, delivery.EventId, result.Error, delivery.NextAttemptAt);
                continue;
            }

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
