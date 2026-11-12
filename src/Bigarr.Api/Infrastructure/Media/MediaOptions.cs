namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Bound from <c>Bigarr:Media</c> (env: <c>Bigarr__Media__FfprobePath</c>…).</summary>
public sealed class MediaOptions
{
    public const string Section = "Bigarr:Media";

    /// <summary>Executable name looked up on the PATH, or a full path.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    public int ProbeTimeoutSeconds { get; set; } = 60;

    /// <summary>Probe files that were scanned or imported without media info, in the background.</summary>
    public bool BackfillEnabled { get; set; } = true;

    public int BackfillInitialDelaySeconds { get; set; } = 90;

    public int BackfillIntervalMinutes { get; set; } = 60;

    /// <summary>Files probed per backfill pass.</summary>
    public int BackfillBatchSize { get; set; } = 25;
}
