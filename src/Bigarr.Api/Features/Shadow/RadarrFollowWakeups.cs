using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Passes asked by Radarr's webhook, run in the background so Radarr gets its answer at once. Radarr sends events in
/// bursts (a film added, then grabbed seconds later by its search on add): one pass per Radarr and burst, a few seconds
/// after the first event. The event wakes the pass; Radarr's history stays what is read, except for a film added or deleted,
/// which history does not carry: that event asks the pass to read the whole film list. A webhook that never arrives costs
/// nothing but time: the scheduled pass catches up.
/// </summary>
public sealed class RadarrFollowWakeups(IServiceScopeFactory scopes, ILogger<RadarrFollowWakeups> logger) : BackgroundService
{
    /// <summary>How long a burst of Radarr events is gathered before the pass.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    private readonly Channel<(int Id, bool FullList)> _queue = Channel.CreateUnbounded<(int, bool)>();

    public void Wake(int followedRadarrId, bool fullList = false) => _queue.Writer.TryWrite((followedRadarrId, fullList));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var first in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await Task.Delay(Debounce, stoppingToken);

            var ids = new SortedDictionary<int, bool> { [first.Id] = first.FullList };
            while (_queue.Reader.TryRead(out var next))
            {
                ids[next.Id] = next.FullList || ids.GetValueOrDefault(next.Id);
            }

            foreach (var (id, fullList) in ids)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                    if (!await db.FollowedRadarrs.AnyAsync(f => f.Id == id && f.Enabled, stoppingToken))
                    {
                        continue;
                    }

                    var pass = await scope.ServiceProvider.GetRequiredService<RadarrFollower>().SyncAsync(id, stoppingToken, fullList);
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
