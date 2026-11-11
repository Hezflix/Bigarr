namespace Bigarr.Api.Features.Shadow;

public enum SeriesCompareOutcome
{
    /// <summary>Sonarr grabbed the release Bigarr picked.</summary>
    Same = 1,

    /// <summary>Both chose for the same episodes of the same series and version within the window, not the same release.</summary>
    Different = 2,

    /// <summary>Bigarr picked, Sonarr grabbed nothing for those episodes around then.</summary>
    BigarrOnly = 3,

    /// <summary>Sonarr grabbed, Bigarr picked nothing for those episodes around then.</summary>
    SonarrOnly = 4,
}

/// <summary>
/// Puts Sonarr's grabs and Bigarr's series picks side by side, per series and version (TVDB id + version name, so deletions
/// and shows Bigarr does not have yet still pair), the series twin of <see cref="ComparePairing"/>. A row is about an
/// episode set rather than one release, since either side may take a season pack where the other takes single episodes:
/// <list type="number">
/// <item>the same release first (guid, info hash or title) within the window: one grab, one pick, "same";</item>
/// <item>then grabs and picks whose episodes overlap within the window form groups (a Sonarr pack with the Bigarr singles
/// of its season, a Bigarr pack with Sonarr's singles): each group is one "different" row with every release of both
/// sides covering distinct episodes; a release that covers episodes its side already has in the group (a later upgrade)
/// is "also picked" for a pick, its own row for a grab;</item>
/// <item>a pick made while a paired grab of the same episodes was still downloading is "also picked" on that row, as for
/// films; the rest are one-sided rows.</item>
/// </list>
/// Pure, so the rules are tested on their own.
/// </summary>
public static class SeriesComparePairing
{
    public sealed record Grab(int Id, int TvdbId, string Version, DateTime At, string? Guid, string? InfoHash, string Title, DateTime? ResolvedAt, bool InFlight, IReadOnlySet<(int Season, int Episode)> Episodes);

    public sealed record Pick(int Id, int TvdbId, string Version, DateTime At, string? Guid, string? InfoHash, string Title, IReadOnlySet<(int Season, int Episode)> Episodes);

    public sealed record Row(SeriesCompareOutcome Outcome, IReadOnlyList<Grab> Sonarr, IReadOnlyList<Pick> Bigarr, IReadOnlyList<Pick> AlsoPicked)
    {
        /// <summary>When the row happened: the earliest release of either side.</summary>
        public DateTime At => Sonarr.Select(g => g.At).Concat(Bigarr.Select(p => p.At)).Min();

        public int TvdbId => Sonarr.Count > 0 ? Sonarr[0].TvdbId : Bigarr[0].TvdbId;

        public string Version => Sonarr.Count > 0 ? Sonarr[0].Version : Bigarr[0].Version;

        /// <summary>Every episode either side covers on this row.</summary>
        public IReadOnlyList<(int Season, int Episode)> Episodes =>
            Sonarr.SelectMany(g => g.Episodes).Concat(Bigarr.SelectMany(p => p.Episodes)).Distinct().OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
    }

    public static List<Row> Match(IEnumerable<Grab> grabs, IEnumerable<Pick> picks, TimeSpan window, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(grabs);
        ArgumentNullException.ThrowIfNull(picks);

        var rows = new List<Row>();
        var picksByKey = picks.ToLookup(p => (p.TvdbId, p.Version));
        var grabsByKey = grabs.ToLookup(g => (g.TvdbId, g.Version));
        foreach (var key in grabsByKey.Select(g => g.Key).Union(picksByKey.Select(p => p.Key)).ToList())
        {
            rows.AddRange(MatchOne([.. grabsByKey[key].OrderBy(g => g.At).ThenBy(g => g.Id)], [.. picksByKey[key].OrderBy(p => p.At).ThenBy(p => p.Id)], window, now));
        }

        return rows;
    }

    private static List<Row> MatchOne(List<Grab> grabs, List<Pick> picks, TimeSpan window, DateTime now)
    {
        var rows = new List<Row>();
        var usedGrabs = new HashSet<int>();
        var usedPicks = new HashSet<int>();
        bool Within(Grab grab, Pick pick) => (pick.At - grab.At).Duration() <= window;
        bool Overlap(IReadOnlySet<(int, int)> a, IReadOnlySet<(int, int)> b) => a.Count == 0 || b.Count == 0 ? a.Count == b.Count : a.Overlaps(b);

        // 1. The same release, wherever it sits in the window: the strongest signal.
        foreach (var grab in grabs)
        {
            var same = picks
                .Where(p => !usedPicks.Contains(p.Id) && Within(grab, p) && SameRelease(grab, p))
                .OrderBy(p => (p.At - grab.At).Duration())
                .ThenBy(p => p.Id)
                .FirstOrDefault();
            if (same is not null)
            {
                usedGrabs.Add(grab.Id);
                usedPicks.Add(same.Id);
                rows.Add(new Row(SeriesCompareOutcome.Same, [grab], [same], []));
            }
        }

        // 2. Groups of grabs and picks linked by overlapping episodes within the window.
        var openGrabs = grabs.Where(g => !usedGrabs.Contains(g.Id)).ToList();
        var openPicks = picks.Where(p => !usedPicks.Contains(p.Id)).ToList();
        var groupOfGrab = new Dictionary<int, int>();
        var groupOfPick = new Dictionary<int, int>();
        var next = 0;
        foreach (var seed in openGrabs)
        {
            if (groupOfGrab.ContainsKey(seed.Id))
            {
                continue;
            }

            var group = next++;
            var queue = new Queue<object>([seed]);
            groupOfGrab[seed.Id] = group;
            while (queue.Count > 0)
            {
                switch (queue.Dequeue())
                {
                    case Grab grab:
                        foreach (var pick in openPicks.Where(p => !groupOfPick.ContainsKey(p.Id) && Within(grab, p) && Overlap(grab.Episodes, p.Episodes)))
                        {
                            groupOfPick[pick.Id] = group;
                            queue.Enqueue(pick);
                        }

                        break;
                    case Pick pick:
                        foreach (var grab in openGrabs.Where(g => !groupOfGrab.ContainsKey(g.Id) && Within(g, pick) && Overlap(g.Episodes, pick.Episodes)))
                        {
                            groupOfGrab[grab.Id] = group;
                            queue.Enqueue(grab);
                        }

                        break;
                }
            }
        }

        var also = new Dictionary<Row, List<Pick>>(ReferenceEqualityComparer.Instance);
        for (var group = 0; group < next; group++)
        {
            var groupGrabs = openGrabs.Where(g => groupOfGrab.GetValueOrDefault(g.Id, -1) == group).ToList();
            var groupPicks = openPicks.Where(p => groupOfPick.GetValueOrDefault(p.Id, -1) == group).ToList();
            if (groupPicks.Count == 0)
            {
                continue;
            }

            // Each side keeps releases covering distinct episodes, earliest first; a later one on the same episodes is an
            // upgrade or a second try: an extra pick goes under "also picked", an extra grab gets its own row.
            var mainGrabs = Disjoint(groupGrabs, g => g.Episodes);
            var mainPicks = Disjoint(groupPicks, p => p.Episodes);
            var row = new Row(SeriesCompareOutcome.Different, mainGrabs, mainPicks, []);
            also[row] = groupPicks.Where(p => !mainPicks.Contains(p)).ToList();
            rows.Add(row);
            usedGrabs.UnionWith(mainGrabs.Select(g => g.Id));
            usedPicks.UnionWith(groupPicks.Select(p => p.Id));
        }

        // 3. A pick made while a paired grab of the same episodes was downloading belongs to that grab's row.
        var paired = rows.Where(r => r.Sonarr.Count > 0 && r.Bigarr.Count > 0).ToList();
        var alone = new List<Pick>();
        foreach (var pick in picks.Where(p => !usedPicks.Contains(p.Id)))
        {
            var owner = paired.LastOrDefault(r => r.Sonarr.Any(g => g.At <= pick.At && pick.At <= End(g, window, now) && Overlap(g.Episodes, pick.Episodes)));
            if (owner is not null)
            {
                if (!also.TryGetValue(owner, out var list))
                {
                    also[owner] = list = [];
                }

                list.Add(pick);
            }
            else
            {
                alone.Add(pick);
            }
        }

        var result = rows.Select(r => also.TryGetValue(r, out var extra) && extra.Count > 0 ? r with { AlsoPicked = extra.OrderBy(p => p.At).ToList() } : r).ToList();
        result.AddRange(grabs.Where(g => !usedGrabs.Contains(g.Id)).Select(g => new Row(SeriesCompareOutcome.SonarrOnly, [g], [], [])));
        result.AddRange(alone.Select(p => new Row(SeriesCompareOutcome.BigarrOnly, [], [p], [])));
        return result;
    }

    private static List<T> Disjoint<T>(List<T> releases, Func<T, IReadOnlySet<(int, int)>> episodes)
    {
        var taken = new HashSet<(int, int)>();
        var kept = new List<T>();
        foreach (var release in releases)
        {
            var covers = episodes(release);
            if (kept.Count == 0 || (covers.Count > 0 && !covers.Overlaps(taken)))
            {
                kept.Add(release);
                taken.UnionWith(covers);
            }
        }

        return kept;
    }

    /// <summary>Until Sonarr's download ended (imported, failed), or now while it is in flight, and at least the window.</summary>
    private static DateTime End(Grab grab, TimeSpan window, DateTime now)
    {
        var end = grab.ResolvedAt ?? (grab.InFlight ? now : grab.At);
        var windowEnd = grab.At + window;
        return end > windowEnd ? end : windowEnd;
    }

    public static bool SameRelease(Grab grab, Pick pick)
    {
        ArgumentNullException.ThrowIfNull(grab);
        ArgumentNullException.ThrowIfNull(pick);

        return (!string.IsNullOrWhiteSpace(grab.Guid) && string.Equals(grab.Guid.Trim(), pick.Guid?.Trim(), StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(grab.InfoHash) && string.Equals(grab.InfoHash, pick.InfoHash, StringComparison.OrdinalIgnoreCase))
            || string.Equals(grab.Title.Trim(), pick.Title.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
