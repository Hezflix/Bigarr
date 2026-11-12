namespace Bigarr.Domain.Quality;

/// <summary>
/// Specifications are kept as raw JSON in the TRaSH / Radarr schema so guide files import unchanged.
/// Matching is evaluated at read time, never stored.
/// </summary>
public sealed class CustomFormat
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool IncludeWhenRenaming { get; set; }

    public required string SpecificationsJson { get; set; }

    /// <summary>Identifier from the TRaSH guides when imported from there; a re-import updates the same row.</summary>
    public string? TrashId { get; set; }

    /// <summary>The guide's suggested scores by profile family, kept as JSON for display. Nothing is computed from it.</summary>
    public string? TrashScoresJson { get; set; }

    /// <summary>
    /// What an import changed or could not evaluate (a Sonarr specification Bigarr lacks, a source only approximated): the
    /// format is kept whole and marked rather than silently altered.
    /// </summary>
    public string? Note { get; set; }

    public ICollection<ProfileFormatScore> Scores { get; set; } = [];
}
