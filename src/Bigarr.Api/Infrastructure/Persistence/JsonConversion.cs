using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bigarr.Api.Infrastructure.Persistence;

/// <summary>Stores a complex property as a JSON text column (blueprint: constraints, images, media info…).</summary>
public static class JsonConversion
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder)
    {
        builder.HasConversion(new JsonValueConverter<T>(), new JsonValueComparer<T>());
        builder.HasColumnType("TEXT");
        return builder;
    }

    public sealed class JsonValueConverter<T>() : ValueConverter<T, string>(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<T>(v, Options)!);

    public sealed class JsonValueComparer<T>() : ValueComparer<T>(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
}
