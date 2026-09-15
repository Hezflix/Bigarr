using System.Text.RegularExpressions;

namespace Bigarr.Domain.Library;

/// <summary>What counts as a video file for the scan and the import, and what looks like a sample.</summary>
public static partial class VideoFiles
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".flv", ".ogm", ".divx", ".vob",
    };

    public static bool IsVideo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Extensions.Contains(Path.GetExtension(path));
    }

    /// <summary>"sample" as a word in the file name: <c>Movie-sample.mkv</c>, <c>sample.mkv</c>, not <c>Sampler.mkv</c>.</summary>
    public static bool LooksLikeSample(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return SampleRegex().IsMatch(Path.GetFileNameWithoutExtension(fileName));
    }

    [GeneratedRegex(@"(?:^|[^a-z0-9])sample(?:[^a-z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SampleRegex();
}
