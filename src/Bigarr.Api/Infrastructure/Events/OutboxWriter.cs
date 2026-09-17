using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>
/// The only way a slice publishes an event: an <see cref="OutboxEvent"/> row added to the current unit of work, so it
/// commits with the business change or not at all (blueprint §3.9). The dispatcher is woken by the save interceptor.
/// </summary>
public sealed class OutboxWriter(BigarrDbContext db, TimeProvider time)
{
    public OutboxEvent Publish(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var row = new OutboxEvent
        {
            Type = domainEvent.Type,
            Payload = EventJson.Serialize(domainEvent),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.Outbox.Add(row);
        return row;
    }
}
