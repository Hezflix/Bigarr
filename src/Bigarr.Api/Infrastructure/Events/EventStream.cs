using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>One dispatched event as the SSE stream sends it: the type as the event name, the payload as the data.</summary>
public sealed record ServerEvent(long Id, string Type, string Payload, DateTime OccurredAt);

/// <summary>Fan-out of dispatched events to the browsers listening on <c>GET /api/v1/events</c>. In memory: a late listener starts from now.</summary>
public sealed class EventStream
{
    private readonly ConcurrentDictionary<Guid, Channel<ServerEvent>> _subscribers = new();

    public int Subscribers => _subscribers.Count;

    public void Publish(ServerEvent serverEvent)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(serverEvent);
        }
    }

    public async IAsyncEnumerable<ServerEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        _subscribers[id] = channel;
        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var serverEvent))
                {
                    yield return serverEvent;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
