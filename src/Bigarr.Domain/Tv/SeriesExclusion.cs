namespace Bigarr.Domain.Tv;

/// <summary>Shows the user never wants added again (Sonarr's import list exclusions), the series twin of <c>MovieExclusion</c>.</summary>
public sealed class SeriesExclusion
{
    public int Id { get; set; }

    public int TvdbId { get; set; }

    public required string Title { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
