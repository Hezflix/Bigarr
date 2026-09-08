namespace Bigarr.Domain.Quality;

/// <summary>Profile × custom format → score. The only place a score is stored.</summary>
public sealed class ProfileFormatScore
{
    public int QualityProfileId { get; set; }

    public QualityProfile QualityProfile { get; set; } = null!;

    public int CustomFormatId { get; set; }

    public CustomFormat CustomFormat { get; set; } = null!;

    public int Score { get; set; }
}
