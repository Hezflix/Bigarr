namespace Bigarr.Domain.Quality;

/// <summary>
/// One entry of the ordered <see cref="QualityProfile.Items"/> list. Either a single quality
/// (<see cref="QualityIds"/> has one element, no group) or a named group of qualities ranked together.
/// The position in the list IS the rank.
/// </summary>
public sealed class QualityProfileItem
{
    /// <summary>Group id (Radarr convention: 1000+). Null for a single quality.</summary>
    public int? GroupId { get; set; }

    public string? GroupName { get; set; }

    public List<int> QualityIds { get; set; } = [];

    public bool Allowed { get; set; }
}
