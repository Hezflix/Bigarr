using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Passes asked by Radarr's webhook, run in the background so Radarr gets its answer at once. Radarr sends events in
/// bursts (a film added, then grabbed seconds later by its search on add): one pass per Radarr and burst, after
/// <see cref="ShadowOptions.WebhookDebounceSeconds"/>. The event only wakes the pass; Radarr's history stays what is read,
/// so a webhook that never arrives costs nothing but time: the scheduled pass catches up.
/// </summary>
public sealed class RadarrFollowWakeups(IServiceScopeFactory scopes, IOptions<ShadowOptions> options, ILogger<RadarrFollowWakeups> logger) : BackgroundService
{
    private readonly Channel<int> _queue = Channel.CreateUnbounded<int>();

    public void Wake(int followedRadarrId) => _queue.Writer.TryWrite(followedRadarrId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var first in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var debounce = TimeSpan.FromSeconds(Math.Max(0, options.Value.WebhookDebounceSeconds));
            if (debounce > TimeSpan.Zero)
            {
                await Task.Delay(debounce, stoppingToken);
            }

            var ids = new SortedSet<int> { first };
            while (_queue.Reader.TryRead(out var next))
            {
                ids.Add(next);
            }

            foreach (var id in ids)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                    if (!await db.FollowedRadarrs.AnyAsync(f => f.Id == id && f.Enabled, stoppingToken))
                    {
                        continue;
                    }

                    var pass = await scope.ServiceProvider.GetRequiredService<RadarrFollower>().SyncAsync(id, stoppingToken);
                    logger.LogDebug("Webhook pass over {Name}: {Summary}", pass.Name, pass.Summary);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The pass asked by Radarr's webhook for followed Radarr {Id} failed", id);
                }
            }
        }
    }
}
