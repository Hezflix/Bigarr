using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;

namespace Bigarr.Domain.Tv;

/// <summary>
/// A physical file of a series version. One file can hold several episodes (<c>S01E01E02</c>): the episode targets
/// point here, never the other way round. Created by the series scan and the import.
/// </summary>
public sealed class EpisodeFile
{
    public int Id { get; set; }

    public int VersionId { get; set; }

    public SeriesVersion Version { get; set; } = null!;

    /// <summary>Relative to the version's folder = RootFolder.Path + SeriesVersion.FolderName (season folder included).</summary>
    public required string RelativePath { get; set; }

    public int SeasonNumber { get; set; }

    public long Size { get; set; }

    [SetWhenAdded]
    public DateTime DateAdded { get; set; }

    public int QualityId { get; set; }

    public QualityDefinition Quality { get; set; } = null!;

    public Revision Revision { get; set; } = new();

    public string? ReleaseGroup { get; set; }

    /// <summary>Language names as the parser uses them ("English", "Japanese"): Bazarr reads <c>languages[].name</c>.</summary>
    public List<string> Languages { get; set; } = [];

    public IndexerFlags IndexerFlags { get; set; }

    /// <summary>Original release name (Sonarr's <c>sceneName</c>), precious to re-parse later.</summary>
    public string? OriginalReleaseTitle { get; set; }

    /// <summary>Filled by ffprobe after the scan or import, nullable like a film's.</summary>
    public MediaInfo? MediaInfo { get; set; }

    public ICollection<EpisodeTarget> Episodes { get; set; } = [];

    /// <summary>Subtitles and other side files imported with it.</summary>
    public ICollection<EpisodeExtraFile> Extras { get; set; } = [];
}
