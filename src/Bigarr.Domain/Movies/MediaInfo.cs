namespace Bigarr.Domain.Movies;

/// <summary>Subset of ffprobe output kept on the file. Nullable until the backfill job has run (§3.4).</summary>
public sealed class MediaInfo
{
    public string? ContainerFormat { get; set; }

    public string? VideoCodec { get; set; }

    public long? VideoBitrate { get; set; }

    public int? VideoBitDepth { get; set; }

    public string? VideoDynamicRange { get; set; }

    public string? VideoDynamicRangeType { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? ScanType { get; set; }

    public string? AudioCodec { get; set; }

    public long? AudioBitrate { get; set; }

    public double? AudioChannels { get; set; }

    public List<string> AudioLanguages { get; set; } = [];

    public List<string> Subtitles { get; set; } = [];

    public TimeSpan? RunTime { get; set; }
}
