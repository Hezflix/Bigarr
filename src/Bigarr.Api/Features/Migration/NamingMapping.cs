using System.Text.Json;
using Bigarr.Domain.Naming;

namespace Bigarr.Api.Features.Migration;

/// <summary>What a followed Radarr or Sonarr says about colons, in Bigarr's terms.</summary>
internal static class NamingMapping
{
    /// <summary>
    /// Radarr's or Sonarr's <c>colonReplacementFormat</c>: Sonarr sends the id (0 to 5), Radarr 5 the camelCase name
    /// (<c>spaceDashSpace</c>). Null when absent or not a value Bigarr knows; <see cref="ColonReplacement.Custom"/> only counts
    /// when <paramref name="allowCustom"/> (Radarr has none).
    /// </summary>
    public static ColonReplacement? Colon(JsonElement value, bool allowCustom)
    {
        ColonReplacement? parsed = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var id) && Enum.IsDefined((ColonReplacement)id) => (ColonReplacement)id,
            JsonValueKind.String when Enum.TryParse<ColonReplacement>(value.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named) => named,
            _ => null,
        };
        return parsed == ColonReplacement.Custom && !allowCustom ? null : parsed;
    }
}
