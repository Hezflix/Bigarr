namespace Bigarr.Domain.Quality;

/// <summary>
/// Specifications are kept as raw JSON in the TRaSH / Radarr schema so guide files import unchanged.
/// Matching is evaluated at read time, never stored (§3.4).
/// </summary>
public sealed class CustomFormat
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool IncludeWhenRenaming { get; set; }

    public required string SpecificationsJson { get; set; }

    public ICollection<ProfileFormatScore> Scores { get; set; } = [];
}
