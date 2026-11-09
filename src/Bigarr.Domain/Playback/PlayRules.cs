namespace Bigarr.Domain.Playback;

/// <summary>
/// What turns a session into a play, after Tautulli's rules so both count the same while they run side by side:
/// a minimum real play time, a watched threshold (or the credits, whichever comes first), an end snapped to the
/// duration when it stops in the last seconds, and a resumed view grouped with the unfinished one before it.
/// </summary>
public sealed class PlayRules(PlaybackSettings settings)
{
    /// <summary>A stop this close to the end counts as the end (Tautulli: 10 s).</summary>
    public static readonly TimeSpan SnapToEnd = TimeSpan.FromSeconds(10);

    public PlaybackSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Seconds a session must last, pauses excluded. A share of an unknown duration falls back to the default seconds.</summary>
    public int MinimumSeconds(long? durationMs) => Settings.MinimumPlayUnit switch
    {
        MinimumPlayUnit.Percent when durationMs is > 0 => (int)Math.Ceiling(durationMs.Value / 1000d * Settings.MinimumPlay / 100d),
        MinimumPlayUnit.Percent => PlaybackSettings.DefaultMinimumPlay,
        _ => Settings.MinimumPlay,
    };

    public bool IsLongEnough(int playedSeconds, long? durationMs) => playedSeconds >= MinimumSeconds(durationMs);

    /// <summary>An end offset in the last seconds of the item becomes the duration.</summary>
    public static long SnapEnd(long offsetMs, long? durationMs) =>
        durationMs is > 0 && durationMs.Value - offsetMs <= SnapToEnd.TotalMilliseconds ? Math.Max(offsetMs, durationMs.Value) : offsetMs;

    /// <summary>Reached the watched share of the duration, or the first credits marker when it comes earlier.</summary>
    public bool IsWatched(long offsetMs, long? durationMs, long? creditsStartMs)
    {
        if (durationMs is not > 0)
        {
            return false;
        }

        var threshold = durationMs.Value * Settings.WatchedPercent / 100d;
        if (creditsStartMs is > 0 && creditsStartMs.Value < threshold)
        {
            threshold = creditsStartMs.Value;
        }

        return offsetMs >= threshold;
    }

    /// <summary>
    /// A new stream continues the previous play of the same item by the same user when that play was not finished and
    /// the stream ends at or after where it stopped (Tautulli's group history).
    /// </summary>
    public static bool Continues(Play previous, long endOffsetMs)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return !previous.Watched && previous.MaxOffsetMs <= endOffsetMs;
    }
}
