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

/// <summary>Webhook deliveries were created or made due: the sender should look.</summary>
public sealed class WebhookSignal : Signal;
