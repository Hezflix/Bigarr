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

    /// <summary>
    /// Files probed per backfill pass (clamped to 1–500), shared between film and episode files. ffprobe reads only the
    /// headers of a local file, well under a second each, so 200 files take a few minutes of an hourly pass and get
    /// through about 4,800 files a day: a library of thousands of episodes is read in days, not weeks.
    /// </summary>
    public int BackfillBatchSize { get; set; } = 200;

    /// <summary>Hours before the backfill tries again a file it could not read.</summary>
    public int BackfillRetryHours { get; set; } = 24;
}
