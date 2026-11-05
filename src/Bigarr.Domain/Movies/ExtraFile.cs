namespace Bigarr.Domain.Movies;

public enum ExtraFileKind
{
    Subtitle = 1,

    /// <summary>Any other extension of the media management list: keeps its extension, no language.</summary>
    Other = 2,
}

/// <summary>
/// A side file Bigarr brought in with a movie file (subtitles, mostly). Tracked so renames, upgrades and deletions keep
/// it in step with its movie file. Files Bigarr did not import (Bazarr's subtitles) are never tracked nor touched.
/// </summary>
public sealed class ExtraFile
{
    public int Id { get; set; }

    public int MediaFileId { get; set; }

    public MediaFile MediaFile { get; set; } = null!;

    /// <summary>Relative to the version's resolved folder, like <see cref="MediaFile.RelativePath"/>.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Lower case, with the dot: <c>.srt</c>.</summary>
    public required string Extension { get; set; }

    public ExtraFileKind Kind { get; set; }

    /// <summary>ISO 639-1 code ("en") detected from the source name; null when the name says nothing.</summary>
    public string? Language { get; set; }

    public bool Forced { get; set; }

    /// <summary>SDH / closed captions.</summary>
    public bool HearingImpaired { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
