using System.Globalization;
using System.Text.Json;

namespace Bigarr.Decision.CustomFormats;

/// <summary>Field values arrive as numbers or strings depending on who wrote the JSON; read them leniently.</summary>
internal static class SpecificationFields
{
    public static string RequireString(CustomFormatSpecificationDefinition definition, string formatName, string field)
    {
        var value = GetString(definition, field);
        if (string.IsNullOrEmpty(value))
        {
            throw new CustomFormatParseException($"Custom format '{formatName}', specification '{definition.Name}': field '{field}' is required.");
        }

        return value;
    }

    public static int RequireInt(CustomFormatSpecificationDefinition definition, string formatName, string field) =>
        GetInt(definition, field) ?? throw new CustomFormatParseException($"Custom format '{formatName}', specification '{definition.Name}': field '{field}' must be a number.");

    public static string? GetString(CustomFormatSpecificationDefinition definition, string field)
    {
        if (!definition.Fields.TryGetValue(field, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static int? GetInt(CustomFormatSpecificationDefinition definition, string field)
    {
        if (!definition.Fields.TryGetValue(field, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.Number when value.TryGetDouble(out var d) => (int)d,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    public static double? GetDouble(CustomFormatSpecificationDefinition definition, string field)
    {
        if (!definition.Fields.TryGetValue(field, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public static bool GetBool(CustomFormatSpecificationDefinition definition, string field, bool fallback = false)
    {
        if (!definition.Fields.TryGetValue(field, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var b) => b,
            JsonValueKind.Number when value.TryGetInt32(out var i) => i != 0,
            _ => fallback,
        };
    }
}
