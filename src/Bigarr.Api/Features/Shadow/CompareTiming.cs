namespace Bigarr.Api.Features.Shadow;

/// <summary>When Bigarr's feeds first carried the release the followed app grabbed, against that grab and its import.</summary>
public enum CompareTimingKind
{
    /// <summary>Bigarr saw the release before (or as) the app grabbed it, and did not pick it.</summary>
    SeenBefore,

    /// <summary>Bigarr first saw it after the grab, while the app's download was still in flight.</summary>
    SeenAfterGrab,

    /// <summary>Bigarr first saw it after the app's file was imported: the follow had already brought that file in.</summary>
    SeenAfterImport,

    /// <summary>Bigarr's feeds never carried it.</summary>
    NeverSeen,
}

/// <summary>
/// A short note on a one-sided row (the app grabbed, Bigarr picked nothing) saying whether timing explains it. In shadow
/// mode Bigarr follows the app's files, so a release Bigarr would have taken minutes later is judged against the app's
/// own import of it and rejected as "not an upgrade".
/// </summary>
/// <param name="GapSeconds">Between Bigarr's first sighting and the grab, always positive; 0 when never seen.</param>
/// <param name="Rejection">Seen before the grab: why Bigarr's feed pass turned it down (null: it approved it).</param>
/// <param name="EngineApproved">Bigarr's engine on the release when the grab was read: true when it would have taken it.</param>
public sealed record CompareTimingNote(CompareTimingKind Kind, long GapSeconds, string? Rejection, bool EngineApproved);

public static class CompareTiming
{
    /// <param name="grabbedAt">When the app grabbed the release.</param>
    /// <param name="importedAt">When the app imported it; null while in flight, failed or ignored.</param>
    /// <param name="seenAt">Bigarr's first sighting of the release in its feeds; null when never seen.</param>
    /// <param name="seenRejection">What the feed pass said at that sighting: the first rejection, null when approved.</param>
    /// <param name="engineApproved">Bigarr's engine verdict on the release at grab time, null when not judged.</param>
    public static CompareTimingNote Note(DateTime grabbedAt, DateTime? importedAt, DateTime? seenAt, string? seenRejection, bool? engineApproved)
    {
        var approved = engineApproved == true;
        if (seenAt is not { } seen)
        {
            return new CompareTimingNote(CompareTimingKind.NeverSeen, 0, null, approved);
        }

        var gap = (long)Math.Abs((seen - grabbedAt).TotalSeconds);
        if (seen <= grabbedAt)
        {
            return new CompareTimingNote(CompareTimingKind.SeenBefore, gap, seenRejection, approved);
        }

        var kind = importedAt is { } imported && seen >= imported ? CompareTimingKind.SeenAfterImport : CompareTimingKind.SeenAfterGrab;
        return new CompareTimingNote(kind, gap, null, approved);
    }

    /// <summary>The first thing the sighting held against the release: its unmatched reason, else its first rejection.</summary>
    public static string? SeenRejection(CompareSightingResponse sighting) =>
        !sighting.Matched ? sighting.UnmatchedReason ?? "unmatched"
        : sighting.Verdict is { Approved: false } verdict ? verdict.Rejections.FirstOrDefault() ?? "rejected"
        : null;
}
