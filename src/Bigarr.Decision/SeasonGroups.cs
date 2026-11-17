namespace Bigarr.Decision;

/// <summary>
/// One release group per season, as Sonarr users keep it by hand. A season's group is the release group with the most
/// files in that season of a version, and only when it has at least <see cref="MinimumFiles"/> of them and no other group
/// has as many. Specials (season 0) never have one. The group only breaks ties: quality and custom format score still
/// decide first.
/// </summary>
public static class SeasonGroups
{
    /// <summary>Fewer files than this say nothing about the season's group.</summary>
    public const int MinimumFiles = 3;

    /// <summary>Release groups compare without case: indexers and parsers disagree on "tlc" and "TLC".</summary>
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The season's group from the release group of each of its files (one entry per file, null for an unknown group),
    /// or null when no known group has <see cref="MinimumFiles"/> files or two groups tie for the most.
    /// </summary>
    public static string? Of(int seasonNumber, IEnumerable<string?> fileGroups)
    {
        ArgumentNullException.ThrowIfNull(fileGroups);
        if (seasonNumber <= 0)
        {
            return null;
        }

        var counts = Count(fileGroups);
        if (counts.Count == 0 || counts[0].Files < MinimumFiles || (counts.Count > 1 && counts[1].Files == counts[0].Files))
        {
            return null;
        }

        return counts[0].Group;
    }

    /// <summary>Files per known release group, most first (ties by name, for a stable order).</summary>
    public static List<(string Group, int Files)> Count(IEnumerable<string?> fileGroups)
    {
        ArgumentNullException.ThrowIfNull(fileGroups);
        return fileGroups
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .GroupBy(g => g!.Trim(), Comparer)
            .Select(g => (Group: g.First()!.Trim(), Files: g.Count()))
            .OrderByDescending(g => g.Files)
            .ThenBy(g => g.Group, Comparer)
            .ToList();
    }

    /// <summary>Why a season has no group, for the preview; null when it has one.</summary>
    public static string? WhyNone(int seasonNumber, IReadOnlyList<(string Group, int Files)> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (seasonNumber <= 0)
        {
            return "Specials keep whatever group each one comes from";
        }

        if (counts.Count == 0)
        {
            return "No file of the season has a known release group";
        }

        if (counts[0].Files < MinimumFiles)
        {
            return $"No release group has {MinimumFiles} files in the season yet (most: {counts[0].Group}, {counts[0].Files})";
        }

        return counts.Count > 1 && counts[1].Files == counts[0].Files
            ? $"{counts[0].Group} and {counts[1].Group} tie with {counts[0].Files} files each"
            : null;
    }

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Comparer.Equals(a.Trim(), b.Trim());

    /// <summary>
    /// True when the release's group is the group of every season it covers. Seasons without a group (specials, too
    /// few files) make it false: there is nothing to match.
    /// </summary>
    public static bool Matches(string? releaseGroup, EpisodeCoverage episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        if (string.IsNullOrWhiteSpace(releaseGroup) || episodes.Slots.Count == 0)
        {
            return false;
        }

        return episodes.Slots.Select(s => s.SeasonNumber).Distinct()
            .All(season => episodes.SeasonGroups.TryGetValue(season, out var group) && Same(group, releaseGroup));
    }

    /// <summary>A file (or pick) in place from another group than its season's: what an alignment replaces. Unknown groups count as other.</summary>
    public static bool IsOffGroup(EpisodeSlot slot, EpisodeCoverage episodes)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(episodes);
        return slot.File is { } file
            && episodes.SeasonGroups.TryGetValue(slot.SeasonNumber, out var group)
            && !Same(file.ReleaseGroup, group);
    }

    /// <summary>
    /// The release realigns some of the episodes it covers: alignment is asked for them, the release comes from their
    /// season's group, and at least one of them holds a file from another group. Then the upgrade rules let it replace
    /// those files at equal quality and score, and the same-group rules for a v2 or a repack step aside for them.
    /// </summary>
    public static bool Realigns(ReleaseCandidate candidate, EpisodeCoverage episodes)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Realigns(candidate.ReleaseGroup, episodes);
    }

    /// <inheritdoc cref="Realigns(ReleaseCandidate, EpisodeCoverage)"/>
    public static bool Realigns(string? releaseGroup, EpisodeCoverage episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        return episodes.Slots.Any(s => episodes.AlignSeasons.Contains(s.SeasonNumber))
            && Matches(releaseGroup, episodes)
            && episodes.Slots.Any(s => episodes.AlignSeasons.Contains(s.SeasonNumber) && IsOffGroup(s, episodes));
    }
}
