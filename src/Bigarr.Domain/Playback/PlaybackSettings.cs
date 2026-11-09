namespace Bigarr.Domain.Playback;

public enum MinimumPlayUnit
{
    Seconds = 1,

    /// <summary>A share of the item's duration.</summary>
    Percent = 2,
}

/// <summary>Single row (id 1): what counts as a play and when it counts as watched. Defaults follow Tautulli's.</summary>
public sealed class PlaybackSettings
{
    public const int SingletonId = 1;

    public const int DefaultMinimumPlay = 120;

    public const int DefaultWatchedPercent = 90;

    public int Id { get; set; } = SingletonId;

    /// <summary>A session shorter than this (pauses excluded) is not written.</summary>
    public int MinimumPlay { get; set; } = DefaultMinimumPlay;

    public MinimumPlayUnit MinimumPlayUnit { get; set; } = MinimumPlayUnit.Seconds;

    /// <summary>A play that reaches this share of the duration, or the credits, is watched.</summary>
    public int WatchedPercent { get; set; } = DefaultWatchedPercent;

    public static PlaybackSettings Default() => new();
}
