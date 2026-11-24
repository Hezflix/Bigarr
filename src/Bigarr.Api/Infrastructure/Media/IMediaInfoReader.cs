using Bigarr.Domain.Movies;

namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Reads codecs, channels, HDR and languages from a file on disk. Null when the tool is missing or the file unreadable.</summary>
public interface IMediaInfoReader
{
    bool IsAvailable { get; }

    /// <param name="releaseTitle">Hint for codec naming: a title saying "H.264" is reported as h264, not x264.</param>
    Task<MediaInfo?> ReadAsync(string path, string? releaseTitle, CancellationToken cancellationToken);

    /// <summary>The tool's whole output on the file (ffprobe's streams and format, as JSON), nothing kept or parsed.</summary>
    Task<RawProbe> ProbeAsync(string path, CancellationToken cancellationToken);
}

public enum RawProbeOutcome
{
    Read,
    ToolMissing,
    Failed,
    TimedOut,
}

/// <summary>What one run of the probe gave: its JSON output when <see cref="Outcome"/> is <see cref="RawProbeOutcome.Read"/>, else why not.</summary>
public sealed record RawProbe(RawProbeOutcome Outcome, string? Json = null, string? Error = null)
{
    public static RawProbe Ok(string json) => new(RawProbeOutcome.Read, json);
}
