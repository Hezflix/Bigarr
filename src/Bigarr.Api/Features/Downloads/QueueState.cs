using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Downloads;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Downloads;

/// <summary>
/// The queue is not a table: this is the in-memory projection of what the download clients report,
/// republished after every poll to whoever listens on the SSE stream, plus the bookkeeping the tracker needs
/// between polls (missed polls per grab, client health).
/// </summary>
public sealed class QueueState(IOptions<DownloadsOptions> options)
{
    private readonly ConcurrentDictionary<Guid, Channel<QueueResponse>> _subscribers = new();
    private readonly ConcurrentDictionary<int, int> _misses = new();
    private readonly ConcurrentDictionary<int, ClientHealth> _clients = new();
    private readonly ConcurrentDictionary<int, DateTime> _stalledSince = new();
    private readonly Channel<bool> _refreshRequests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private long _revision;

    /// <summary>Polls never overlap: a manual refresh waits for the scheduled one and vice versa.</summary>
    public SemaphoreSlim PollLock { get; } = new(1, 1);

    public QueueResponse Snapshot { get; private set; } = new(null, null, (int)options.Value.PollInterval.TotalSeconds, 0, [], []);

    public ChannelReader<bool> RefreshRequests => _refreshRequests.Reader;

    /// <summary>Asks the tracker to poll now instead of at the next tick (after a grab, a removal, a client change).</summary>
    public void RequestRefresh() => _refreshRequests.Writer.TryWrite(true);

    public int CountMiss(int grabId) => _misses.AddOrUpdate(grabId, 1, (_, misses) => misses + 1);

    public void Seen(int grabId) => _misses.TryRemove(grabId, out _);

    public void Forget(int grabId)
    {
        _misses.TryRemove(grabId, out _);
        _stalledSince.TryRemove(grabId, out _);
    }

    /// <summary>First poll that saw the grab stalled, this run of the process; the timer restarts after a restart on purpose.</summary>
    public DateTime StalledSince(int grabId, DateTime now) => _stalledSince.GetOrAdd(grabId, now);

    public void NotStalled(int grabId) => _stalledSince.TryRemove(grabId, out _);

    public void RecordClient(int clientId, string name, string? error, DateTime now)
    {
        _clients.AddOrUpdate(
            clientId,
            _ => new ClientHealth(name, error, error is null ? now : null),
            (_, previous) => new ClientHealth(name, error, error is null ? now : previous.LastSuccessAt));
    }

    public void ForgetClient(int clientId) => _clients.TryRemove(clientId, out _);

    public List<QueueClientStatusResponse> ClientStatuses() =>
        _clients.OrderBy(c => c.Value.Name).Select(c => new QueueClientStatusResponse(c.Key, c.Value.Name, c.Value.LastError is null, c.Value.LastError, c.Value.LastSuccessAt)).ToList();

    public QueueResponse Publish(QueueResponse snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var published = snapshot with { Revision = Interlocked.Increment(ref _revision) };
        Snapshot = published;
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(published);
        }

        return published;
    }

    /// <summary>The current snapshot first, then every publication until the listener leaves. Slow listeners only miss intermediate states.</summary>
    public async IAsyncEnumerable<QueueResponse> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<QueueResponse>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _subscribers[id] = channel;
        try
        {
            yield return Snapshot;
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var snapshot))
                {
                    yield return snapshot;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    private sealed record ClientHealth(string Name, string? LastError, DateTime? LastSuccessAt);
}
