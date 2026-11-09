namespace Bigarr.Domain.Tv;

public enum UnmatchedFileReason
{
    /// <summary>The episode parser could not read season/episode numbers, an absolute number or an air date.</summary>
    NotParsable = 1,

    /// <summary>Parsed, but the series has no episode with those numbers (or that air date).</summary>
    EpisodeNotFound = 2,

    /// <summary>The numbers point at more than one episode and nothing tells them apart.</summary>
    Ambiguous = 3,

    /// <summary>A daily-style name in a series that is not daily (Sonarr rejects it the same way).</summary>
    WrongSeriesType = 4,

    /// <summary>Another file of the same version already holds one of these episodes.</summary>
    Duplicate = 5,

    /// <summary>A season pack folder name or an extra: not one episode file.</summary>
    NotAnEpisode = 6,
}

/// <summary>
/// A video file inside a series folder that the scan could not attach to episodes. Kept, never dropped, until a scan
/// no longer sees it or it gets matched; the file itself is never touched.
/// </summary>
public sealed class UnmatchedEpisodeFile
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series Series { get; set; } = null!;

    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    /// <summary>Relative to the version folder.</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public UnmatchedFileReason Reason { get; set; }

    /// <summary>What the parser saw, or the candidate episodes.</summary>
    public string? Detail { get; set; }

    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
