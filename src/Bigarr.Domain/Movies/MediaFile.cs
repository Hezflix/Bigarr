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

    [SetWhenAdded]
    public DateTime DateAdded { get; set; }

    public int QualityId { get; set; }

    public QualityDefinition Quality { get; set; } = null!;

    public Revision Revision { get; set; } = new();

    public string? Edition { get; set; }

    public string? ReleaseGroup { get; set; }

    /// <summary>Language names as the parser and Radarr use them ("English", "French"): Bazarr reads <c>languages[].name</c>.</summary>
    public List<string> Languages { get; set; } = [];

    public IndexerFlags IndexerFlags { get; set; }

    /// <summary>
    /// The grab this file was imported from; null for a file found by a scan, imported by hand without a grab, or brought
    /// over from Radarr/Sonarr. Tells the interactive search which release is on disk, as a pack's files carry their own names.
    /// </summary>
    public int? GrabId { get; set; }

    /// <summary>Original release name, precious to re-parse later.</summary>
    public string? OriginalReleaseTitle { get; set; }

    public MediaInfo? MediaInfo { get; set; }

    /// <summary>When the backfill last tried to read this file's media info and got nothing: it waits before trying again.</summary>
    public DateTime? MediaInfoTriedAt { get; set; }

    /// <summary>
    /// The media info is the one Radarr or Sonarr gave when the file was imported from it, not yet read by Bigarr: the
    /// backfill still probes the file and replaces it, and a probe that reads nothing keeps it.
    /// </summary>
    public bool MediaInfoFromArr { get; set; }

    /// <summary>Side files imported with it: they follow its renames and leave with it.</summary>
    public List<ExtraFile> Extras { get; set; } = [];
}
