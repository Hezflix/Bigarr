using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Quality;

namespace Bigarr.Domain.Movies;

/// <summary>The physical file of a version: at most one (an upgrade replaces through the recycle bin).</summary>
public sealed class MediaFile
{
    public int Id { get; set; }

    public int VersionId { get; set; }

    public MovieVersion Version { get; set; } = null!;

    /// <summary>Relative to the version's resolved folder = RootFolder.Path + MovieVersion.FolderName.</summary>
    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public int QualityId { get; set; }

    public QualityDefinition Quality { get; set; } = null!;

    public Revision Revision { get; set; } = new();

    public string? Edition { get; set; }

    public string? ReleaseGroup { get; set; }

    /// <summary>Language names as the parser and Radarr use them ("English", "French"): Bazarr reads <c>languages[].name</c>.</summary>
    public List<string> Languages { get; set; } = [];

    public IndexerFlags IndexerFlags { get; set; }

    /// <summary>Original release name, precious to re-parse later.</summary>
    public string? OriginalReleaseTitle { get; set; }

    public MediaInfo? MediaInfo { get; set; }
}
