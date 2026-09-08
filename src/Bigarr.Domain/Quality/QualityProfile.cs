namespace Bigarr.Domain.Quality;

public sealed class QualityProfile
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Ordered list of qualities and groups with their allowed flag. The order is the rank.</summary>
    public List<QualityProfileItem> Items { get; set; } = [];

    /// <summary>Quality id or group id at which the version is considered complete.</summary>
    public int Cutoff { get; set; }

    public bool UpgradeAllowed { get; set; } = true;

    public int MinFormatScore { get; set; }

    public int CutoffFormatScore { get; set; }

    /// <summary>Minimum score gain for an upgrade at equal quality rank (§3.6). A higher rank always passes.</summary>
    public int MinUpgradeFormatScore { get; set; }

    /// <summary>ISO 639-1 code. Non-null: Bazarr reads it from the profile.</summary>
    public required string PreferredLanguage { get; set; }

    /// <summary>Seeded profiles (e.g. <c>Any</c>) cannot be deleted.</summary>
    public bool IsSystem { get; set; }

    public ICollection<ProfileFormatScore> FormatScores { get; set; } = [];
}
