using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Tv;

/// <summary>
/// A side file imported with an episode file (subtitles, the release's .nfo), named after it and tracked with it: it is
/// renamed with the file and leaves with it through the recycle bin. The series twin of <see cref="ExtraFile"/>.
/// </summary>
public sealed class EpisodeExtraFile
{
    public int Id { get; set; }

    public int EpisodeFileId { get; set; }

    public EpisodeFile EpisodeFile { get; set; } = null!;

    /// <summary>Relative to the version folder, like <see cref="EpisodeFile.RelativePath"/>.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Lower case, with the dot: <c>.srt</c>.</summary>
    public required string Extension { get; set; }

    public ExtraFileKind Kind { get; set; }

    /// <summary>ISO 639-1 code read from the source name; null when the name says nothing.</summary>
    public string? Language { get; set; }

    public bool Forced { get; set; }

    public bool HearingImpaired { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
