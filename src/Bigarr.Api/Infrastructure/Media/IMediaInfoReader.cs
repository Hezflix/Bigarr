using Bigarr.Domain.Movies;

namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Reads codecs, channels, HDR and languages from a file on disk. Null when the tool is missing or the file unreadable.</summary>
public interface IMediaInfoReader
{
    bool IsAvailable { get; }

    /// <param name="releaseTitle">Hint for codec naming: a title saying "H.264" is reported as h264, not x264.</param>
    Task<MediaInfo?> ReadAsync(string path, string? releaseTitle, CancellationToken cancellationToken);
}
