namespace Bigarr.Api.Infrastructure.Downloads;

/// <summary>Bound from <c>Bigarr:Downloads</c> (env: <c>Bigarr__Downloads__PollSeconds</c>…).</summary>
public sealed class DownloadsOptions
{
    public const string Section = "Bigarr:Downloads";

    public const int MinimumPollSeconds = 2;

    /// <summary>How often the download clients are asked for their state. The queue cannot be fresher than this.</summary>
    public int PollSeconds { get; set; } = 10;

    /// <summary>A tracked download absent from its client for this many consecutive polls is considered removed by hand.</summary>
    public int RemovedAfterPolls { get; set; } = 3;

    /// <summary>Grace period after startup before the first poll.</summary>
    public int InitialDelaySeconds { get; set; } = 5;

    public bool Enabled { get; set; } = true;

    /// <summary>Where the mock client writes the files of a completed torrent. Default: <c>mock-downloads</c> under the config directory.</summary>
    public string? MockDownloadDirectory { get; set; }

    /// <summary>A failed download is removed from its client (files included) once blocklisted, like Radarr's "remove failed".</summary>
    public bool RemoveFailedDownloads { get; set; } = true;

    /// <summary>A failed download queues an automatic search for a replacement (failed handling).</summary>
    public bool SearchAfterFailure { get; set; } = true;

    /// <summary>A download stalled for this long is treated as failed. 0 leaves stalled torrents alone, as Radarr does.</summary>
    public int StalledTimeoutMinutes { get; set; }

    public TimeSpan? StalledTimeout => StalledTimeoutMinutes > 0 ? TimeSpan.FromMinutes(StalledTimeoutMinutes) : null;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(MinimumPollSeconds, PollSeconds));
}
