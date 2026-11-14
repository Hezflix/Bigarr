using Bigarr.Api.Features.Shadow;
using Bigarr.Domain.Shadow;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>The timing note on a one-sided row: seen before the grab, after it (before or after the import), or never.</summary>
public sealed class CompareTimingTests
{
    private static readonly DateTime Grab = new(2026, 11, 13, 17, 3, 0, DateTimeKind.Utc);

    [Fact]
    public void Seen_minutes_before_the_grab_and_rejected_names_the_reason()
    {
        var note = CompareTiming.Note(Grab, Grab.AddMinutes(2), Grab.AddMinutes(-4), "Not enough seeders: 0 (minimum 1)", engineApproved: true);

        Assert.Equal(new CompareTimingNote(CompareTimingKind.SeenBefore, 240, "Not enough seeders: 0 (minimum 1)", true), note);
    }

    [Fact]
    public void Seen_after_the_import_is_judged_against_the_app_own_file()
    {
        var note = CompareTiming.Note(Grab, Grab.AddMinutes(2), Grab.AddMinutes(4), null, engineApproved: true);

        Assert.Equal(CompareTimingKind.SeenAfterImport, note.Kind);
        Assert.Equal(240, note.GapSeconds);
        Assert.Null(note.Rejection);
        Assert.True(note.EngineApproved);
    }

    [Fact]
    public void Seen_after_the_grab_while_the_download_is_in_flight()
    {
        var note = CompareTiming.Note(Grab, importedAt: null, Grab.AddMinutes(10), null, engineApproved: false);

        Assert.Equal(CompareTimingKind.SeenAfterGrab, note.Kind);
        Assert.Equal(600, note.GapSeconds);
        Assert.False(note.EngineApproved);
    }

    [Fact]
    public void Seen_after_the_grab_but_before_the_import()
    {
        var note = CompareTiming.Note(Grab, Grab.AddMinutes(30), Grab.AddMinutes(5), null, engineApproved: true);

        Assert.Equal(CompareTimingKind.SeenAfterGrab, note.Kind);
    }

    [Fact]
    public void Seen_at_the_grab_second_counts_as_before()
    {
        Assert.Equal(CompareTimingKind.SeenBefore, CompareTiming.Note(Grab, null, Grab, null, null).Kind);
    }

    [Fact]
    public void Never_seen_keeps_the_engine_verdict()
    {
        var note = CompareTiming.Note(Grab, Grab.AddMinutes(2), seenAt: null, null, engineApproved: true);

        Assert.Equal(new CompareTimingNote(CompareTimingKind.NeverSeen, 0, null, true), note);
        Assert.False(CompareTiming.Note(Grab, null, null, null, null).EngineApproved);
    }

    [Fact]
    public void Sighting_rejection_is_the_unmatched_reason_else_the_first_rejection()
    {
        var seenAt = Grab.AddMinutes(-4);
        Assert.Equal("Unknown series", CompareTiming.SeenRejection(new CompareSightingResponse(seenAt, "Demo", "t", false, "Unknown series", null, null)));
        Assert.Equal("Not enough seeders: 0 (minimum 1)", CompareTiming.SeenRejection(new CompareSightingResponse(seenAt, "Demo", "t", true, null, "1080p",
            new SightingVerdict("1080p", false, ["Not enough seeders: 0 (minimum 1)", "Other"], 29))));
        Assert.Null(CompareTiming.SeenRejection(new CompareSightingResponse(seenAt, "Demo", "t", true, null, "1080p", new SightingVerdict("1080p", true, [], 29))));
    }
}
