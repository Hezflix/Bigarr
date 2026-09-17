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

    public static string Serialize(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);
    }
}
