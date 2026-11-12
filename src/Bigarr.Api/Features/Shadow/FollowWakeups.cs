using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Persistence;

namespace Bigarr.Api.Features.Shadow;

/// <summary>
/// Passes asked by a followed *arr's webhook, run in the background so the *arr gets its answer at once. Events come in
/// bursts (an item added, then grabbed seconds later by its search on add): one pass per followed instance and burst, a
/// few seconds after the first event. The event wakes the pass; the *arr's history stays what is read, except for an item
/// added or deleted, which history does not carry: that event asks the pass to read the whole list. A webhook that never
/// arrives costs nothing but time: the scheduled pass catches up.
/// </summary>
public abstract class FollowWakeups(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
{
    /// <summary>How long a burst of events is gathered before the pass.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    private readonly Channel<(int Id, bool FullList)> _queue = Channel.CreateUnbounded<(int, bool)>();

    /// <summary>The followed app's name in the logs: "Radarr", "Sonarr".</summary>
    protected abstract string Source { get; }

    protected abstract Task<bool> IsEnabledAsync(BigarrDbContext db, int id, CancellationToken ct);

    /// <summary>Runs one pass and says what it did, for the debug log.</summary>
    protected abstract Task<(string Name, string Summary)> PassAsync(IServiceProvider services, int id, bool fullList, CancellationToken ct);

    public void Wake(int followedId, bool fullList = false) => _queue.Writer.TryWrite((followedId, fullList));

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
                    if (!await IsEnabledAsync(scope.ServiceProvider.GetRequiredService<BigarrDbContext>(), id, stoppingToken))
                    {
                        continue;
                    }

                    var pass = await PassAsync(scope.ServiceProvider, id, fullList, stoppingToken);
                    logger.LogDebug("Webhook pass over {Name}: {Summary}", pass.Name, pass.Summary);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The pass asked by {Source}'s webhook for followed {Source} {Id} failed", Source, Source, id);
                }
            }
        }
    }
}

/// <summary>
/// What the follow remembers of each followed instance between two passes, in memory only. After a restart, or a pass
/// that failed, nothing is remembered and the next pass reads the whole list, so nothing is missed.
/// </summary>
public abstract class FollowMemory<TEntry>
    where TEntry : class
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TEntry> _entries = new();

    public TEntry? Get(int followedId) => _entries.GetValueOrDefault(followedId);

    public void Remember(int followedId, TEntry entry) => _entries[followedId] = entry;

    public void Forget(int followedId) => _entries.TryRemove(followedId, out _);
}
