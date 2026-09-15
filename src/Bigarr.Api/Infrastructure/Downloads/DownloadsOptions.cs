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

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(MinimumPollSeconds, PollSeconds));
}
