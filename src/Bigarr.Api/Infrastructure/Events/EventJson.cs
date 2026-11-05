using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Domain.Events;

namespace Bigarr.Api.Infrastructure.Events;

/// <summary>How event payloads are written: camelCase, enums as strings, same as the API.</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, Type> Types = DomainEventCatalog.Entries.ToDictionary(e => e.Type, e => e.Clr, StringComparer.Ordinal);

    public static string Serialize(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);
    }

    /// <summary>The record behind an outbox row, or null when its type left the catalogue or its JSON no longer reads.</summary>
    public static DomainEvent? Deserialize(string type, string payloadJson)
    {
        if (!Types.TryGetValue(type, out var clr))
        {
            return null;
        }

        try
        {
            return (DomainEvent?)JsonSerializer.Deserialize(payloadJson, clr, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
