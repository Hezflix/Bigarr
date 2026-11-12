using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bigarr.Decision.CustomFormats;
using Bigarr.Decision.CustomFormats.Specifications;
using Bigarr.Domain.Acquisition;

namespace Bigarr.Api.Features.Migration;

/// <summary>
/// Sonarr's custom formats in Bigarr's terms. Bigarr's engine uses Radarr's numbering (it evaluates films and episodes
/// alike), so the values that differ are translated, from Sonarr's develop branch at
/// <c>c018578e4b79ee048575c02d7a8aa2b929e4b837</c> (2026-11-03):
/// <list type="bullet">
/// <item>indexer flags (<c>Parser/Model/IndexerFlags.cs</c>): Freeleech 1, Halfleech 2, DoubleUpload 4 keep their bit;
/// Internal 8 → 32, Scene 16 → 128, Freeleech75 32 → 256, Freeleech25 64 → 512, Nuked 128 → 2048; Subtitles 256 has no
/// counterpart;</item>
/// <item>languages (<c>Languages/Language.cs</c>): matched by name, ids 0–25 are the same, the rest move (Arabic 26 → 31,
/// Hindi 27 → 26…), Azerbaijani, Uzbek, Malay and Irish are Bigarr's 1001–1004;</item>
/// <item>sources (<c>Qualities/QualitySource.cs</c>): Television 1 → TV, Web 3 → WEBDL, WebRip 4 → WEBRIP, DVD 5 → DVD,
/// Bluray 6 → BLURAY; TelevisionRaw 2 and BlurayRaw 7 become TV and BLURAY, broader than Sonarr's (Bigarr tells raw-HD and
/// remux by modifier), and the format is marked;</item>
/// <item>release type: Sonarr's own specification, evaluated by Bigarr as is.</item>
/// </list>
/// A specification Bigarr cannot evaluate (an unknown implementation, the Subtitles flag, a language it lacks) is kept as an
/// <see cref="UnsupportedSpecification"/> that never matches, and the format carries a note saying so: never silently dropped.
/// </summary>
public static class SonarrCustomFormats
{
    public sealed record Translated(int SonarrId, CustomFormatDefinition Definition, List<string> Notes)
    {
        /// <summary>The definition as one comparable string: specifications sorted, fields sorted.</summary>
        public string Key => KeyOf(Definition.Specifications);
    }

    private static readonly Dictionary<int, IndexerFlags> FlagBits = new()
    {
        [1] = IndexerFlags.Freeleech,
        [2] = IndexerFlags.Halfleech,
        [4] = IndexerFlags.DoubleUpload,
        [8] = IndexerFlags.Internal,
        [16] = IndexerFlags.Scene,
        [32] = IndexerFlags.Freeleech75,
        [64] = IndexerFlags.Freeleech25,
        [128] = IndexerFlags.Nuked,
    };

    /// <summary>Sonarr's Subtitles flag (256): Radarr's numbering, hence Bigarr's, has no such flag.</summary>
    public const int SonarrSubtitlesFlag = 256;

    /// <summary>Sonarr language id → Bigarr (Radarr) language id, matched on the name.</summary>
    private static readonly Dictionary<int, int> Languages = new()
    {
        [-2] = -2,
        [0] = 0,
        [1] = 1,
        [2] = 2,
        [3] = 3,
        [4] = 4,
        [5] = 5,
        [6] = 6,
        [7] = 7,
        [8] = 8,
        [9] = 9,
        [10] = 10,
        [11] = 11,
        [12] = 12,
        [13] = 13,
        [14] = 14,
        [15] = 15,
        [16] = 16,
        [17] = 17,
        [18] = 18,
        [19] = 19,
        [20] = 20,
        [21] = 21,
        [22] = 22,
        [23] = 23,
        [24] = 24,
        [25] = 25,
        [26] = 31, // Arabic
        [27] = 26, // Hindi
        [28] = 29, // Bulgarian
        [29] = 48, // Malayalam
        [30] = 32, // Ukrainian
        [31] = 35, // Slovak
        [32] = 28, // Thai
        [33] = 30, // Portuguese (Brazil)
        [34] = 37, // Spanish (Latino)
        [35] = 27, // Romanian
        [36] = 36, // Latvian
        [37] = 33, // Persian
        [38] = 38, // Catalan
        [39] = 39, // Croatian
        [40] = 40, // Serbian
        [41] = 41, // Bosnian
        [42] = 42, // Estonian
        [43] = 43, // Tamil
        [44] = 44, // Indonesian
        [45] = 46, // Macedonian
        [46] = 47, // Slovenian
        [47] = 1001, // Azerbaijani
        [48] = 1002, // Uzbek
        [49] = 1003, // Malay
        [50] = 54, // Urdu
        [51] = 55, // Romansh
        [52] = 57, // Georgian
        [53] = 1004, // Irish
    };

    /// <summary>Bigarr's flags of a Sonarr flag set (a file's, a release's); Subtitles is dropped, it has no counterpart.</summary>
    public static IndexerFlags Flags(int sonarrFlags)
    {
        var flags = IndexerFlags.None;
        foreach (var (bit, flag) in FlagBits)
        {
            if ((sonarrFlags & bit) != 0)
            {
                flags |= flag;
            }
        }

        return flags;
    }

    /// <summary>Sonarr's flags as its history writes them: enum names (<c>Freeleech, Internal</c>) or the number.</summary>
    public static IndexerFlags ParseFlags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IndexerFlags.None;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
        {
            return Flags(bits);
        }

        // Sonarr's names are Bigarr's names (only the numbers differ).
        var flags = IndexerFlags.None;
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<IndexerFlags>(token, ignoreCase: true, out var flag) && Enum.IsDefined(flag))
            {
                flags |= flag;
            }
        }

        return flags;
    }

    /// <summary>One custom format as Sonarr's <c>/api/v3/customformat</c> returns it, in Bigarr's terms.</summary>
    public static Translated Translate(JsonElement format)
    {
        var id = format.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var value) ? value : 0;
        var name = format.TryGetProperty("name", out var nameElement) ? nameElement.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CustomFormatParseException("A Sonarr custom format has no name.");
        }

        var notes = new List<string>();
        var specifications = new List<CustomFormatSpecificationDefinition>();
        if (format.TryGetProperty("specifications", out var specs) && specs.ValueKind == JsonValueKind.Array)
        {
            foreach (var spec in specs.EnumerateArray())
            {
                specifications.Add(TranslateSpecification(spec, name, notes));
            }
        }

        if (specifications.Count == 0)
        {
            throw new CustomFormatParseException($"Sonarr custom format '{name}' has no specification.");
        }

        var includeWhenRenaming = format.TryGetProperty("includeCustomFormatWhenRenaming", out var include) && include.ValueKind == JsonValueKind.True;
        return new Translated(id, new CustomFormatDefinition(0, name, includeWhenRenaming, specifications), notes);
    }

    private static CustomFormatSpecificationDefinition TranslateSpecification(JsonElement spec, string formatName, List<string> notes)
    {
        var implementation = spec.TryGetProperty("implementation", out var impl) ? impl.GetString() ?? string.Empty : string.Empty;
        var specName = spec.TryGetProperty("name", out var n) ? n.GetString() ?? implementation : implementation;
        var negate = spec.TryGetProperty("negate", out var neg) && neg.ValueKind == JsonValueKind.True;
        var required = spec.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True;
        var fields = Fields(spec);

        CustomFormatSpecificationDefinition Make(string implementationName, Dictionary<string, JsonElement> values) =>
            new(specName, implementationName, negate, required, values);

        CustomFormatSpecificationDefinition Unsupported(string reason)
        {
            notes.Add($"'{specName}' ({implementation}) is not evaluated by Bigarr: {reason}");
            return Make(UnsupportedSpecification.ImplementationName, new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["implementation"] = JsonSerializer.SerializeToElement(implementation),
                ["reason"] = JsonSerializer.SerializeToElement(reason),
                ["original"] = JsonSerializer.SerializeToElement(spec.GetRawText()),
            });
        }

        int? Int(string field) => fields.TryGetValue(field, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
        Dictionary<string, JsonElement> With(string field, int value) => new(fields, StringComparer.OrdinalIgnoreCase) { [field] = JsonSerializer.SerializeToElement(value) };

        CustomFormatSpecificationDefinition result;
        switch (implementation)
        {
            case "ReleaseTitleSpecification":
            case "ReleaseGroupSpecification":
            case "ResolutionSpecification":
            case "SizeSpecification":
            case "ReleaseTypeSpecification":
            case "YearSpecification":
                result = Make(implementation, fields);
                break;
            case "IndexerFlagSpecification":
                if (Int("value") is not { } flag)
                {
                    return Unsupported("no flag value");
                }

                if (flag == SonarrSubtitlesFlag)
                {
                    return Unsupported("Sonarr's Subtitles flag has no counterpart in Bigarr's indexer flags");
                }

                if (!FlagBits.TryGetValue(flag, out var bigarrFlag))
                {
                    return Unsupported($"unknown Sonarr flag {flag}");
                }

                result = Make(implementation, With("value", (int)bigarrFlag));
                break;
            case "LanguageSpecification":
                if (Int("value") is not { } language || !Languages.TryGetValue(language, out var bigarrLanguage))
                {
                    return Unsupported($"Sonarr language {Int("value")?.ToString(CultureInfo.InvariantCulture) ?? "?"} has no counterpart in Bigarr");
                }

                result = Make(implementation, With("value", bigarrLanguage));
                break;
            case "SourceSpecification":
                switch (Int("value"))
                {
                    case 1: result = Make(implementation, With("value", 6)); break; // Television → TV
                    case 3: result = Make(implementation, With("value", 7)); break; // Web → WEBDL
                    case 4: result = Make(implementation, With("value", 8)); break; // WebRip → WEBRIP
                    case 5: result = Make(implementation, With("value", 5)); break; // DVD
                    case 6: result = Make(implementation, With("value", 9)); break; // Bluray → BLURAY
                    case 2:
                        notes.Add($"'{specName}': Sonarr's TelevisionRaw source is read as TV (raw-HD is a modifier in Bigarr), so it also matches plain HDTV.");
                        result = Make(implementation, With("value", 6));
                        break;
                    case 7:
                        notes.Add($"'{specName}': Sonarr's BlurayRaw source is read as BLURAY (remux is a modifier in Bigarr), so it also matches plain Bluray encodes.");
                        result = Make(implementation, With("value", 9));
                        break;
                    case 0: result = Make(implementation, With("value", 0)); break;
                    default: return Unsupported($"unknown Sonarr source {Int("value")?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
                }

                break;
            default:
                return Unsupported("Bigarr has no such specification");
        }

        try
        {
            CustomFormatSpecificationFactory.Create(result, formatName);
            return result;
        }
        catch (CustomFormatParseException ex)
        {
            return Unsupported(ex.Message);
        }
    }

    /// <summary>Sonarr's <c>fields</c>: an array of <c>{name, value, …}</c> (the API) or an object (an export).</summary>
    private static Dictionary<string, JsonElement> Fields(JsonElement spec)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (!spec.TryGetProperty("fields", out var raw))
        {
            return fields;
        }

        if (raw.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in raw.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } key && item.TryGetProperty("value", out var value))
                {
                    fields[key] = value.Clone();
                }
            }
        }
        else if (raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in raw.EnumerateObject())
            {
                fields[property.Name] = property.Value.Clone();
            }
        }

        return fields;
    }

    /// <summary>A definition as one comparable string: specifications sorted, fields by key, labels left out.</summary>
    public static string KeyOf(IEnumerable<CustomFormatSpecificationDefinition> specifications)
    {
        var array = new JsonArray();
        // A specification's name is a label: two formats alike but for their labels are the same format.
        var ordered = specifications.Select(spec => (Spec: spec, Fields: string.Join("|", spec.Fields.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Select(f => $"{f.Key.ToLowerInvariant()}={f.Value.GetRawText()}"))))
            .OrderBy(x => x.Spec.Implementation, StringComparer.Ordinal).ThenBy(x => x.Fields, StringComparer.Ordinal).ThenBy(x => x.Spec.Negate).ThenBy(x => x.Spec.Required)
            .Select(x => x.Spec);
        foreach (var spec in ordered)
        {
            var fields = new JsonObject();
            foreach (var (key, value) in spec.Fields.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                fields[key.ToLowerInvariant()] = JsonNode.Parse(value.GetRawText());
            }

            array.Add(new JsonObject
            {
                ["implementation"] = spec.Implementation,
                ["negate"] = spec.Negate,
                ["required"] = spec.Required,
                ["fields"] = fields,
            });
        }

        return array.ToJsonString();
    }
}
