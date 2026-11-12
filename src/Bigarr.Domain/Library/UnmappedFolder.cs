namespace Bigarr.Domain.Library;

public enum UnmappedReason
{
    NoVideoFiles = 1,
    NotParsable = 2,
    NoMatch = 3,
    AmbiguousMatch = 4,
    MetadataError = 5,
}

/// <summary>A first-level folder of a root folder the scan could not attach to a film. Feeds the manual import screen.</summary>
public sealed class UnmappedFolder
{
    public int Id { get; set; }

    public int RootFolderId { get; set; }

    public RootFolder RootFolder { get; set; } = null!;

    /// <summary>Folder name, relative to the root.</summary>
    public required string RelativePath { get; set; }

    public int FileCount { get; set; }

    public UnmappedReason Reason { get; set; }

    /// <summary>What the parser saw, or the candidates that made the match ambiguous.</summary>
    public string? Detail { get; set; }

    [SetWhenAdded]
    public DateTime DiscoveredAt { get; set; }

    [SetWhenAdded]
    public DateTime LastSeenAt { get; set; }
}
