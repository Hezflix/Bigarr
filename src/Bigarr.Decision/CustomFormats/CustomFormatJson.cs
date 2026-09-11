using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bigarr.Decision.CustomFormats;

/// <summary>
/// Reads the JSON the TRaSH guides publish and the JSON Radarr exports, which differ only in the shape of
/// <c>fields</c> (an object for TRaSH, an array of <c>{name, value}</c> for Radarr). Writes the object shape.
/// </summary>
public static class CustomFormatJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>One definition or an array of them.</summary>
    public static IReadOnlyList<CustomFormatDefinition> ParseMany(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new CustomFormatParseException($"Not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().Select(e => Parse(e, 0)).ToList(),
                JsonValueKind.Object => [Parse(root, 0)],
                _ => throw new CustomFormatParseException("Expected a custom format object or an array of them."),
            };
        }
    }

    public static CustomFormatDefinition Parse(JsonElement element, int id)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CustomFormatParseException("A custom format must be a JSON object.");
        }

        var name = GetString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CustomFormatParseException("A custom format needs a name.");
        }

        if (!TryGetProperty(element, "specifications", out var specs) || specs.ValueKind != JsonValueKind.Array)
        {
            throw new CustomFormatParseException($"Custom format '{name}' has no specifications array.");
        }

        var specifications = specs.EnumerateArray().Select(s => ParseSpecification(s, name)).ToList();
        if (specifications.Count == 0)
        {
            throw new CustomFormatParseException($"Custom format '{name}' has no specification: it would match everything.");
        }

        IReadOnlyDictionary<string, int>? trashScores = null;
        if (TryGetProperty(element, "trash_scores", out var scores) && scores.ValueKind == JsonValueKind.Object)
        {
            trashScores = scores.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out _))
                .ToDictionary(p => p.Name, p => p.Value.GetInt32(), StringComparer.OrdinalIgnoreCase);
        }

        return new CustomFormatDefinition(
            id,
            name.Trim(),
            GetBool(element, "includeCustomFormatWhenRenaming") ?? GetBool(element, "includeWhenRenaming") ?? false,
            specifications,
            GetString(element, "trash_id"),
            trashScores);
    }

    /// <summary>Only the specifications, as stored on the entity: an array in the object-fields shape.</summary>
    public static IReadOnlyList<CustomFormatSpecificationDefinition> ParseSpecifications(string json, string formatName)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new CustomFormatParseException($"Custom format '{formatName}': stored specifications are not an array.");
        }

        return document.RootElement.EnumerateArray().Select(s => ParseSpecification(s, formatName)).ToList();
    }

    public static string SerializeSpecifications(IEnumerable<CustomFormatSpecificationDefinition> specifications)
    {
        var array = new JsonArray();
        foreach (var spec in specifications)
        {
            var fields = new JsonObject();
            foreach (var (key, value) in spec.Fields)
            {
                fields[key] = JsonNode.Parse(value.GetRawText());
            }

            array.Add(new JsonObject
            {
                ["name"] = spec.Name,
                ["implementation"] = spec.Implementation,
                ["negate"] = spec.Negate,
                ["required"] = spec.Required,
                ["fields"] = fields,
            });
        }

        return array.ToJsonString(Options);
    }

    public static string SerializeScores(IReadOnlyDictionary<string, int>? scores) =>
        JsonSerializer.Serialize(scores ?? new Dictionary<string, int>(), Options);

    public static IReadOnlyDictionary<string, int>? ParseScores(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CustomFormatSpecificationDefinition ParseSpecification(JsonElement element, string formatName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new CustomFormatParseException($"Custom format '{formatName}': a specification must be an object.");
        }

        var implementation = GetString(element, "implementation");
        if (string.IsNullOrWhiteSpace(implementation))
        {
            throw new CustomFormatParseException($"Custom format '{formatName}': a specification has no implementation.");
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (TryGetProperty(element, "fields", out var rawFields))
        {
            switch (rawFields.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in rawFields.EnumerateObject())
                    {
                        fields[property.Name] = property.Value.Clone();
                    }

                    break;
                case JsonValueKind.Array:
                    // Radarr's export: [{ "name": "value", "value": "..." }, ...]
                    foreach (var item in rawFields.EnumerateArray())
                    {
                        var fieldName = GetString(item, "name");
                        if (!string.IsNullOrEmpty(fieldName) && TryGetProperty(item, "value", out var value))
                        {
                            fields[fieldName] = value.Clone();
                        }
                    }

                    break;
                case JsonValueKind.Null:
                    break;
                default:
                    throw new CustomFormatParseException($"Custom format '{formatName}': specification fields must be an object or an array.");
            }
        }

        var definition = new CustomFormatSpecificationDefinition(
            GetString(element, "name") ?? implementation,
            implementation.Trim(),
            GetBool(element, "negate") ?? false,
            GetBool(element, "required") ?? false,
            fields);

        // Compiles the specification once so a bad implementation or regex is refused at import, not at search time.
        CustomFormatSpecificationFactory.Create(definition, formatName);
        return definition;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? GetBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
