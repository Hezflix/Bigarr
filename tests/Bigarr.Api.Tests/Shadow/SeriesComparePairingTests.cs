using Bigarr.Api.Features.Shadow;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>The series pairing rules on their own: same release, a pack against singles either way, upgrades, one-sided rows.</summary>
public sealed class SeriesComparePairingTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private static HashSet<(int, int)> Eps(int season, params int[] numbers) => numbers.Select(n => (season, n)).ToHashSet();

    private static SeriesComparePairing.Grab Grab(int id, double hours, string title, HashSet<(int, int)> episodes, string version = "1080p", bool inFlight = false, DateTime? resolved = null) =>
        new(id, 81189, version, T0.AddHours(hours), null, null, title, resolved, inFlight, episodes);

    private static SeriesComparePairing.Pick Pick(int id, double hours, string title, HashSet<(int, int)> episodes, string version = "1080p") =>
        new(id, 81189, version, T0.AddHours(hours), null, null, title, episodes);

    [Fact]
    public void The_same_pack_on_both_sides_is_one_same_row()
    {
        var rows = SeriesComparePairing.Match([Grab(1, 1, "Show.S01.1080p-A", Eps(1, 1, 2, 3))], [Pick(1, 0, "show.s01.1080p-a", Eps(1, 1, 2, 3))], Window, T0.AddDays(1));
        var row = Assert.Single(rows);
        Assert.Equal(SeriesCompareOutcome.Same, row.Outcome);
    }

    [Fact]
    public void A_sonarr_pack_and_bigarr_singles_of_its_episodes_are_one_different_row()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 2, "Show.S01.1080p-A", Eps(1, 1, 2, 3))],
            [Pick(1, 0, "Show.S01E01.1080p-B", Eps(1, 1)), Pick(2, 0.5, "Show.S01E02.1080p-B", Eps(1, 2)), Pick(3, 1, "Show.S01E03.1080p-B", Eps(1, 3))],
            Window,
            T0.AddDays(1));
        var row = Assert.Single(rows);
        Assert.Equal(SeriesCompareOutcome.Different, row.Outcome);
        Assert.Single(row.Sonarr);
        Assert.Equal(3, row.Bigarr.Count);
        Assert.Equal(3, row.Episodes.Count);
    }

    [Fact]
    public void A_bigarr_pack_and_sonarr_singles_are_one_different_row_and_an_upgrade_is_also_picked()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 1, "Show.S01E01.720p-A", Eps(1, 1)), Grab(2, 1.5, "Show.S01E02.720p-A", Eps(1, 2))],
            [Pick(1, 0, "Show.S01.1080p-B", Eps(1, 1, 2)), Pick(2, 3, "Show.S01.1080p.REPACK-B", Eps(1, 1, 2))],
            Window,
            T0.AddDays(1));
        var row = Assert.Single(rows);
        Assert.Equal(SeriesCompareOutcome.Different, row.Outcome);
        Assert.Equal(2, row.Sonarr.Count);
        Assert.Equal(1, Assert.Single(row.Bigarr).Id);
        Assert.Equal(2, Assert.Single(row.AlsoPicked).Id);
    }

    [Fact]
    public void Unrelated_episodes_versions_or_times_stay_one_sided()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 0, "Show.S01E01-A", Eps(1, 1)), Grab(2, 0, "Show.S01E05-A", Eps(1, 5), version: "4k")],
            [Pick(1, 0, "Show.S01E02-B", Eps(1, 2)), Pick(2, 72, "Show.S01E05-B", Eps(1, 5), version: "4k")],
            Window,
            T0.AddDays(5));
        Assert.Equal(2, rows.Count(r => r.Outcome == SeriesCompareOutcome.SonarrOnly));
        Assert.Equal(2, rows.Count(r => r.Outcome == SeriesCompareOutcome.BigarrOnly));
    }

    [Fact]
    public void A_pick_while_sonarr_downloads_the_same_episodes_is_also_picked()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 0, "Show.S01E01-A", Eps(1, 1), inFlight: true)],
            [Pick(1, 0.5, "Show.S01E01-B", Eps(1, 1)), Pick(2, 48, "Show.S01E01.PROPER-B", Eps(1, 1))],
            Window,
            T0.AddDays(3));
        var row = Assert.Single(rows);
        Assert.Equal(SeriesCompareOutcome.Different, row.Outcome);
        Assert.Equal(2, Assert.Single(row.AlsoPicked).Id);
    }

    private static readonly DateTime Later = T0.AddDays(30);

    [Fact]
    public void A_pick_days_before_sonarrs_grab_is_bigarr_first_not_two_lone_rows()
    {
        var different = Assert.Single(SeriesComparePairing.Match([Grab(1, 72, "Show.S01E01.BluRay-A", Eps(1, 1))], [Pick(1, 0, "Show.S01E01.WEB-B", Eps(1, 1))], Window, Later));
        Assert.Equal(SeriesCompareOutcome.Different, different.Outcome);
        Assert.Single(different.Sonarr);
        Assert.Single(different.Bigarr);
        Assert.Equal(T0, different.At);

        var same = Assert.Single(SeriesComparePairing.Match([Grab(1, 72, "Show.S01E01.WEB-B", Eps(1, 1))], [Pick(1, 0, "show.s01e01.web-b", Eps(1, 1))], Window, Later));
        Assert.Equal(SeriesCompareOutcome.Same, same.Outcome);

        var rows = SeriesComparePairing.Match(
            [Grab(1, 100, "Show.S01E01.BluRay-A", Eps(1, 1))],
            [Pick(1, 0, "Show.S01E01.WEB-B", Eps(1, 1)), Pick(2, 50, "Show.S01E01.PROPER-B", Eps(1, 1))],
            Window,
            Later);
        var paired = Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.Different);
        Assert.Equal(2, Assert.Single(paired.Bigarr).Id);
        Assert.Equal(1, Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.BigarrOnly).Bigarr[0].Id);
    }

    [Fact]
    public void An_earlier_pick_counts_up_to_fourteen_days_before_the_grab()
    {
        var limit = ComparePairing.EarlierPickLimit.TotalHours;
        var paired = Assert.Single(SeriesComparePairing.Match([Grab(1, limit, "Show.S01E01-A", Eps(1, 1))], [Pick(1, 0, "Show.S01E01-B", Eps(1, 1))], Window, Later));
        Assert.Equal(SeriesCompareOutcome.Different, paired.Outcome);

        var rows = SeriesComparePairing.Match([Grab(1, limit + 1, "Show.S01E01-A", Eps(1, 1))], [Pick(1, 0, "Show.S01E01-B", Eps(1, 1))], Window, Later);
        Assert.Equal(1, rows.Count(r => r.Outcome == SeriesCompareOutcome.SonarrOnly));
        Assert.Equal(1, rows.Count(r => r.Outcome == SeriesCompareOutcome.BigarrOnly));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void An_earlier_pick_does_not_jump_over_another_grab_of_the_same_episodes()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 48, "Show.S01E01-A", Eps(1, 1), resolved: T0.AddHours(50)), Grab(2, 120, "Show.S01E01-C", Eps(1, 1))],
            [Pick(1, 0, "Show.S01E01-B", Eps(1, 1))],
            Window,
            Later);
        Assert.Equal(1, Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.Different).Sonarr[0].Id);
        Assert.Equal(2, Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.SonarrOnly).Sonarr[0].Id);

        // A pick made while a paired grab was downloading stays "also picked" on that grab's row.
        var also = SeriesComparePairing.Match(
            [Grab(1, 0, "Show.S01E01-A", Eps(1, 1), resolved: T0.AddHours(60)), Grab(2, 200, "Show.S01E01-C", Eps(1, 1))],
            [Pick(1, 0.1, "Show.S01E01-A", Eps(1, 1)), Pick(2, 40, "Show.S01E01.PROPER-B", Eps(1, 1))],
            Window,
            Later);
        var row = Assert.Single(also, r => r.Outcome == SeriesCompareOutcome.Same);
        Assert.Equal(2, Assert.Single(row.AlsoPicked).Id);
        Assert.Equal(2, Assert.Single(also, r => r.Outcome == SeriesCompareOutcome.SonarrOnly).Sonarr[0].Id);
    }

    [Fact]
    public void An_earlier_pick_of_other_episodes_does_not_count()
    {
        var rows = SeriesComparePairing.Match([Grab(1, 72, "Show.S01E01-A", Eps(1, 1))], [Pick(1, 0, "Show.S01E02-B", Eps(1, 2))], Window, Later);
        Assert.Equal(1, rows.Count(r => r.Outcome == SeriesCompareOutcome.SonarrOnly));
        Assert.Equal(1, rows.Count(r => r.Outcome == SeriesCompareOutcome.BigarrOnly));

        // A grab of E02 between them does not block E01's pairing.
        var both = SeriesComparePairing.Match(
            [Grab(1, 48, "Show.S01E02-A", Eps(1, 2)), Grab(2, 72, "Show.S01E01-A", Eps(1, 1))],
            [Pick(1, 0, "Show.S01E01-B", Eps(1, 1)), Pick(2, 0, "Show.S01E02-B", Eps(1, 2))],
            Window,
            Later);
        Assert.Equal(2, both.Count);
        Assert.All(both, r => Assert.Equal(SeriesCompareOutcome.Different, r.Outcome));
    }

    [Fact]
    public void A_sonarr_pack_pairs_with_the_singles_bigarr_picked_over_the_days_before()
    {
        var rows = SeriesComparePairing.Match(
            [Grab(1, 150, "Show.S01.BluRay-A", Eps(1, 1, 2, 3))],
            [
                Pick(1, 0, "Show.S01E01-B", Eps(1, 1)),
                Pick(2, 48, "Show.S01E02-B", Eps(1, 2)),
                Pick(3, 96, "Show.S01E03-B", Eps(1, 3)),
                Pick(4, 60, "Show.S01E01.PROPER-B", Eps(1, 1)),
            ],
            Window,
            Later);
        var row = Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.Different);
        Assert.Single(row.Sonarr);
        Assert.Equal([2, 4, 3], row.Bigarr.Select(p => p.Id).ToArray());
        Assert.Equal(1, Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.BigarrOnly).Bigarr[0].Id);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Singles_sonarr_grabs_after_a_pack_bigarr_picked_join_one_row()
    {
        var rows = SeriesComparePairing.Match(
            [
                Grab(1, 48, "Show.S01E01-A", Eps(1, 1)),
                Grab(2, 100, "Show.S01E02-A", Eps(1, 2)),
                Grab(3, 150, "Show.S01E03-A", Eps(1, 3)),
                Grab(4, 200, "Show.S01E01.PROPER-A", Eps(1, 1)),
            ],
            [Pick(1, 0, "Show.S01.WEB-B", Eps(1, 1, 2, 3))],
            Window,
            Later);
        var row = Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.Different);
        Assert.Equal([1, 2, 3], row.Sonarr.Select(g => g.Id).ToArray());
        Assert.Single(row.Bigarr);
        Assert.Equal(4, Assert.Single(rows, r => r.Outcome == SeriesCompareOutcome.SonarrOnly).Sonarr[0].Id);
        Assert.Equal(2, rows.Count);
    }
}
