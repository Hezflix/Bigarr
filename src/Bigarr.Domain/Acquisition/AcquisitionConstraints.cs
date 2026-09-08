using System.Text.Json.Serialization;

namespace Bigarr.Domain.Acquisition;

/// <summary>
/// Per-target acquisition constraints, on top of the quality profile. This is what makes two versions
/// sharing a profile distinguishable (Theatrical vs Director's Cut). Stored as JSON.
/// </summary>
public sealed class AcquisitionConstraints
{
    /// <summary>Regular expression matched against the parsed edition, e.g. <c>director'?s cut</c>.</summary>
    public string? Edition { get; set; }

    public List<int> RequiredCustomFormatIds { get; set; } = [];

    public List<int> ForbiddenCustomFormatIds { get; set; } = [];

    /// <summary>Exact release group (case-insensitive).</summary>
    public string? ReleaseGroup { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Edition is null && ReleaseGroup is null && RequiredCustomFormatIds.Count == 0 && ForbiddenCustomFormatIds.Count == 0;

    /// <summary>Used to pick the most specific version when several accept the same release (§3.3).</summary>
    [JsonIgnore]
    public int Specificity =>
        (Edition is null ? 0 : 1) + (ReleaseGroup is null ? 0 : 1) + RequiredCustomFormatIds.Count + ForbiddenCustomFormatIds.Count;
}
