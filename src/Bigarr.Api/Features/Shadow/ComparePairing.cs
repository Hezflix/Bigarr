namespace Bigarr.Api.Features.Shadow;

public enum CompareOutcome
{
    /// <summary>Radarr grabbed the release Bigarr picked.</summary>
    Same = 1,

    /// <summary>Both chose for the same film and version within the window, not the same release.</summary>
    Different = 2,

    /// <summary>Bigarr picked, Radarr grabbed nothing for that film and version around then.</summary>
    BigarrOnly = 3,

    /// <summary>Radarr grabbed, Bigarr picked nothing for that film and version around then.</summary>
    RadarrOnly = 4,
}

/// <summary>
/// Puts Radarr's grabs and Bigarr's picks side by side, per film and version (TMDB id + version name, so deletions and
/// films Bigarr does not have yet still pair). The same release first (guid, info hash or title), then the closest pick in
/// time within the window. A pick made while Radarr's download was still in flight, once the grab is paired, is listed
/// with it ("also picked") rather than as Bigarr-only: Bigarr keeps judging a version Radarr is downloading for, since a
/// pick changes nothing, and Radarr would have refused most of those. Pure, so the rules are tested on their own.
/// </summary>
public static class ComparePairing
{
    public sealed record Grab(int Id, int TmdbId, string Version, DateTime At, string? Guid, string? InfoHash, string Title, DateTime? ResolvedAt, bool InFlight);

    public sealed record Pick(int Id, int TmdbId, string Version, DateTime At, string? Guid, string? InfoHash, string Title);

    public sealed record Pair(CompareOutcome Outcome, Grab? Radarr, Pick? Bigarr, IReadOnlyList<Pick> AlsoPicked)
    {
        /// <summary>When the pair happened: the earlier of the two sides.</summary>
        public DateTime At => Radarr is null ? Bigarr!.At : Bigarr is null ? Radarr.At : Radarr.At < Bigarr.At ? Radarr.At : Bigarr.At;
    }

    public static List<Pair> Match(IEnumerable<Grab> grabs, IEnumerable<Pick> picks, TimeSpan window, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(grabs);
        ArgumentNullException.ThrowIfNull(picks);

        var pairs = new List<Pair>();
        var picksByKey = picks.ToLookup(p => (p.TmdbId, p.Version));
        var grabsByKey = grabs.ToLookup(g => (g.TmdbId, g.Version));
        var keys = grabsByKey.Select(g => g.Key).Union(picksByKey.Select(p => p.Key)).ToList();
        foreach (var key in keys)
        {
            pairs.AddRange(MatchOne([.. grabsByKey[key].OrderBy(g => g.At).ThenBy(g => g.Id)], [.. picksByKey[key].OrderBy(p => p.At).ThenBy(p => p.Id)], window, now));
        }

        return pairs;
    }

    private static List<Pair> MatchOne(List<Grab> grabs, List<Pick> picks, TimeSpan window, DateTime now)
    {
        var used = new HashSet<int>();
        var paired = new Dictionary<int, (Pick Pick, CompareOutcome Outcome)>();

        bool Within(Grab grab, Pick pick) => (pick.At - grab.At).Duration() <= window;

        Pick? Closest(Grab grab, Func<Pick, bool> filter) => picks
            .Where(p => !used.Contains(p.Id) && Within(grab, p) && filter(p))
            .OrderBy(p => (p.At - grab.At).Duration())
            .ThenBy(p => p.Id)
            .FirstOrDefault();

        // The same release first, wherever it sits in the window: it is the strongest signal.
        foreach (var grab in grabs)
        {
            if (Closest(grab, pick => SameRelease(grab, pick)) is { } same)
            {
                used.Add(same.Id);
                paired[grab.Id] = (same, CompareOutcome.Same);
            }
        }

        foreach (var grab in grabs.Where(g => !paired.ContainsKey(g.Id)))
        {
            if (Closest(grab, _ => true) is { } other)
            {
                used.Add(other.Id);
                paired[grab.Id] = (other, CompareOutcome.Different);
            }
        }

        // Picks made while a paired grab was downloading (or in its window) belong to it.
        var also = grabs.ToDictionary(g => g.Id, _ => new List<Pick>());
        var alone = new List<Pick>();
        foreach (var pick in picks.Where(p => !used.Contains(p.Id)))
        {
            var owner = grabs.LastOrDefault(g => paired.ContainsKey(g.Id) && g.At <= pick.At && pick.At <= End(g, window, now));
            if (owner is not null)
            {
                also[owner.Id].Add(pick);
            }
            else
            {
                alone.Add(pick);
            }
        }

        var result = new List<Pair>();
        foreach (var grab in grabs)
        {
            result.Add(paired.TryGetValue(grab.Id, out var match)
                ? new Pair(match.Outcome, grab, match.Pick, also[grab.Id])
                : new Pair(CompareOutcome.RadarrOnly, grab, null, []));
        }

        result.AddRange(alone.Select(pick => new Pair(CompareOutcome.BigarrOnly, null, pick, [])));
        return result;
    }

    /// <summary>Until Radarr's download ended (imported, failed), or now while it is in flight, and at least the window.</summary>
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
