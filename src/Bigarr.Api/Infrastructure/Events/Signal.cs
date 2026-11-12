using System.Threading.Channels;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>An in-process wake-up call: many notifiers, one waiter, coalesced. Never blocks the notifier.</summary>
public abstract class Signal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>True when woken, false when <paramref name="timeout"/> elapsed first.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, TimeProvider time, CancellationToken ct)
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var timer = time.CreateTimer(static state => ((CancellationTokenSource)state!).Cancel(), tick, timeout, Timeout.InfiniteTimeSpan);
        try
        {
            await _channel.Reader.ReadAsync(tick.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>Outbox rows were committed: the dispatcher should look.</summary>
public sealed class OutboxSignal : Signal;

/// <summary>
/// Webhook deliveries were created or made due: the sender should look. Also where a manual retry leaves word for
/// the sender: the sender works on its own copy of a delivery from the moment it loaded its batch until it saves the
/// outcome, so a retry written to the database meanwhile would be overwritten by that outcome (possibly "given up").
/// </summary>
public sealed class WebhookSignal : Signal
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, byte> _retries = new();

    /// <summary>The user asked for this delivery to be sent again, counter back to zero.</summary>
    public void RetryRequested(long deliveryId) => _retries[deliveryId] = 0;

    /// <summary>True, once, when a retry was asked for this delivery since the sender last looked.</summary>
    public bool TakeRetry(long deliveryId) => _retries.TryRemove(deliveryId, out _);
}
