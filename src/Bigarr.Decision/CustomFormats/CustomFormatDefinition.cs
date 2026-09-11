using System.Text.Json;

namespace Bigarr.Decision.CustomFormats;

/// <summary>One rule of a custom format, in Radarr's shape: an implementation name and its fields.</summary>
public sealed record CustomFormatSpecificationDefinition(
    string Name,
    string Implementation,
    bool Negate,
    bool Required,
    IReadOnlyDictionary<string, JsonElement> Fields);

/// <summary>
/// A custom format as the guides publish it (blueprint §3.6): a name and a list of specifications. Scores are not
/// part of it; they live on each quality profile. <see cref="TrashScores"/> keeps the guide's suggestions.
/// </summary>
public sealed record CustomFormatDefinition(
    int Id,
    string Name,
    bool IncludeWhenRenaming,
    IReadOnlyList<CustomFormatSpecificationDefinition> Specifications,
    string? TrashId = null,
    IReadOnlyDictionary<string, int>? TrashScores = null)
{
    /// <summary>The guide's default score, when the definition came from TRaSH.</summary>
    public int? TrashDefaultScore => TrashScores is not null && TrashScores.TryGetValue("default", out var score) ? score : null;
}

public sealed class CustomFormatParseException(string message, Exception? inner = null) : Exception(message, inner);
