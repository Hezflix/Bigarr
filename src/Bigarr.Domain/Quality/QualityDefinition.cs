namespace Bigarr.Domain.Quality;

/// <summary>
/// Seeded table, ids identical to the vendored parser's qualities. Sizes are MB per minute of runtime.
/// No global rank: ranking only exists inside a profile (§3.4).
/// </summary>
public sealed class QualityDefinition
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Display title, editable.</summary>
    public string? Title { get; set; }

    public QualitySource Source { get; set; }

    public int Resolution { get; set; }

    public QualityModifier Modifier { get; set; }

    public double? MinSize { get; set; }

    public double? PreferredSize { get; set; }

    public double? MaxSize { get; set; }
}
