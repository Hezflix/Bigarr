using Bigarr.Api.Features.Shadow;
using static Bigarr.Api.Features.Shadow.ComparePairing;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>How Radarr's grabs and Bigarr's picks are paired: same release first, then the closest in the window, per film and version.</summary>
public sealed class ComparePairingTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly DateTime Now = T0.AddDays(5);

    private static Grab G(int id, double hours, string title, string? guid = null, string? hash = null, int tmdb = 603, string version = "1080p", double? resolvedHours = null, bool inFlight = false) =>
        new(id, tmdb, version, T0.AddHours(hours), guid, hash, title, resolvedHours is { } r ? T0.AddHours(r) : null, inFlight);

    private static Pick P(int id, double hours, string title, string? guid = null, string? hash = null, int tmdb = 603, string version = "1080p") =>
        new(id, tmdb, version, T0.AddHours(hours), guid, hash, title);

    [Fact]
    public void The_same_release_is_recognised_by_guid_info_hash_or_title()
    {
        var pairs = Match(
            [G(1, 0, "A", guid: "g-1"), G(2, 0, "B", hash: "ABCDEF", tmdb: 27205), G(3, 0, "The.Film.2010.1080p-GRP", tmdb: 157336)],
            [P(10, 0.25, "A renamed", guid: "g-1"), P(11, -0.5, "B renamed", hash: "abcdef", tmdb: 27205), P(12, 1, "the.film.2010.1080p-grp", tmdb: 157336)],
            Window,
            Now);

        Assert.Equal(3, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(CompareOutcome.Same, p.Outcome));
        Assert.Equal(10, pairs.Single(p => p.Radarr!.Id == 1).Bigarr!.Id);
        Assert.Equal(11, pairs.Single(p => p.Radarr!.Id == 2).Bigarr!.Id);
        Assert.Equal(12, pairs.Single(p => p.Radarr!.Id == 3).Bigarr!.Id);
    }

    [Fact]
    public void A_different_release_pairs_with_the_closest_pick_in_the_window()
    {
        var pairs = Match([G(1, 0, "Radarr's")], [P(10, -6, "Early"), P(11, 2, "Close")], Window, Now);

        var pair = pairs.Single(p => p.Radarr is not null);
        Assert.Equal(CompareOutcome.Different, pair.Outcome);
        Assert.Equal(11, pair.Bigarr!.Id);
        // The early pick was made before Radarr's grab: it is Bigarr's alone.
        Assert.Equal(10, Assert.Single(pairs, p => p.Outcome == CompareOutcome.BigarrOnly).Bigarr!.Id);
    }

    [Fact]
    public void The_same_release_wins_over_a_closer_different_one()
    {
        var pairs = Match([G(1, 0, "X", guid: "x")], [P(10, 0.1, "Y", guid: "y"), P(11, 5, "X", guid: "x")], Window, Now);

        var pair = pairs.Single(p => p.Radarr is not null);
        Assert.Equal(CompareOutcome.Same, pair.Outcome);
        Assert.Equal(11, pair.Bigarr!.Id);
        // Y was picked while Radarr's grab was in its window: listed with it, not as Bigarr-only.
        Assert.Equal(10, Assert.Single(pair.AlsoPicked).Id);
        Assert.DoesNotContain(pairs, p => p.Outcome == CompareOutcome.BigarrOnly);
    }

    [Fact]
    public void Outside_the_window_each_side_stands_alone()
    {
        var pairs = Match([G(1, 0, "X")], [P(10, 30, "Y")], Window, Now);

        Assert.Equal(CompareOutcome.RadarrOnly, Assert.Single(pairs, p => p.Radarr is not null).Outcome);
        Assert.Equal(CompareOutcome.BigarrOnly, Assert.Single(pairs, p => p.Radarr is null).Outcome);
    }

    [Fact]
    public void Picks_made_while_radarr_downloads_belong_to_the_grab_until_it_lands()
    {
        // Radarr's download took 40 hours: the picks of that time are listed with the grab, the one after it is Bigarr's alone.
        var pairs = Match([G(1, 0, "X", guid: "x", resolvedHours: 40)], [P(10, 0.2, "X", guid: "x"), P(11, 30, "Z"), P(12, 50, "W")], Window, Now);

        var pair = pairs.Single(p => p.Radarr is not null);
        Assert.Equal(CompareOutcome.Same, pair.Outcome);
        Assert.Equal(11, Assert.Single(pair.AlsoPicked).Id);
        Assert.Equal(12, Assert.Single(pairs, p => p.Outcome == CompareOutcome.BigarrOnly).Bigarr!.Id);

        // Still downloading: everything until now is its.
        var inFlight = Match([G(1, 0, "X", guid: "x", inFlight: true)], [P(10, 0.2, "X", guid: "x"), P(11, 30, "Z"), P(12, 50, "W")], Window, Now);
        Assert.Equal(2, inFlight.Single().AlsoPicked.Count);
    }

    [Fact]
    public void Films_and_versions_never_pair_across()
    {
        var pairs = Match([G(1, 0, "X", version: "4k")], [P(10, 0, "X", version: "1080p"), P(11, 0, "X", tmdb: 1)], Window, Now);

        Assert.Equal(3, pairs.Count);
        Assert.Equal(CompareOutcome.RadarrOnly, pairs.Single(p => p.Radarr is not null).Outcome);
        Assert.Equal(2, pairs.Count(p => p.Outcome == CompareOutcome.BigarrOnly));
    }

    [Fact]
    public void Two_grabs_of_one_version_each_take_their_own_pick()
    {
        // A grab, then an upgrade a day and a half later: each pairs with what Bigarr picked around it.
        var pairs = Match([G(1, 0, "Web", guid: "w", resolvedHours: 2), G(2, 36, "Remux", guid: "r")], [P(10, 0.5, "Web", guid: "w"), P(11, 36.2, "Bluray", guid: "b")], Window, Now);

        Assert.Equal(CompareOutcome.Same, pairs.Single(p => p.Radarr!.Id == 1).Outcome);
        var upgrade = pairs.Single(p => p.Radarr!.Id == 2);
        Assert.Equal(CompareOutcome.Different, upgrade.Outcome);
        Assert.Equal(11, upgrade.Bigarr!.Id);
        Assert.Equal(T0.AddHours(36), upgrade.At);
    }
}
