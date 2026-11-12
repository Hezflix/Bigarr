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

    /// <summary>
    /// Set on a file that is a hardlink of a linked film's file (S8): the film engine owns it. The series side never
    /// imports, upgrades or renames it on its own; the link keeper re-creates it when the film's file changes.
    /// </summary>
    public int? SeriesMovieLinkId { get; set; }

    public SeriesMovieLink? SeriesMovieLink { get; set; }

    /// <summary>Absolute path of the film file this one is a hardlink of. Null for an ordinary episode file.</summary>
    public string? LinkedFromPath { get; set; }

    /// <summary>The film's <c>MediaFile</c> it was linked from: a new id means the film was upgraded.</summary>
    public int? LinkedMediaFileId { get; set; }

    public bool IsFilmLink => LinkedFromPath is not null;

    public ICollection<EpisodeTarget> Episodes { get; set; } = [];

    /// <summary>Subtitles and other side files imported with it.</summary>
    public ICollection<EpisodeExtraFile> Extras { get; set; } = [];
}
