namespace Bigarr.Decision;

/// <summary>
/// One episode a release covers, as one series version sees it: its acquisition target (the (version, episode) pair),
/// numbers for the reasons, air date and runtime, the file or pick in place, and the episodes that file also holds.
/// </summary>
public sealed record EpisodeSlot(
    Bigarr.Domain.Acquisition.IAcquisitionTarget Target,
    /// <summary>The shared episode id (the same for every version).</summary>
    int EpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    DateTime? AirDateUtc,
    int? RuntimeMinutes,
    ExistingFile? File,
    bool HasActiveGrab)
{
    /// <summary>Shared ids of every episode the file in place holds (itself included); empty without a file.</summary>
    public IReadOnlyCollection<int> FileEpisodeIds { get; init; } = [];

    public string Label => $"S{SeasonNumber:00}E{EpisodeNumber:00}";
}

/// <summary>
/// Everything about the episodes a release covers for one version (Sonarr's <c>RemoteEpisode.Episodes</c>). A target
/// context carrying this judges the release against all of them at once: one verdict per version and release.
/// </summary>
public sealed record EpisodeCoverage(
    IReadOnlyList<EpisodeSlot> Slots,
    bool IsAnime,
    /// <summary>The version's own switch (Sonarr's series <c>monitored</c>).</summary>
    bool VersionMonitored,
    /// <summary>The series' typical runtime, used for episodes without one.</summary>
    int? SeriesRuntimeMinutes,
    DateTime Now)
{
    /// <summary>Sonarr's season-pack upgrade mode: how many of a pack's episodes must be upgraded.</summary>
    public Bigarr.Domain.Library.SeasonPackUpgrade SeasonPackUpgrade { get; init; } = Bigarr.Domain.Library.SeasonPackUpgrade.All;

    /// <summary>Percent, for the threshold mode.</summary>
    public double SeasonPackUpgradeThreshold { get; init; } = 100;

    /// <summary>The series' first regular season, for Sonarr's runtime guess on a new show.</summary>
    public int? FirstSeasonNumber { get; init; }

    /// <summary>Air date of that season's first episode (the pilot), when known.</summary>
    public DateTime? PilotAirDateUtc { get; init; }

    /// <summary>The version's group of each season that has one (<see cref="Decision.SeasonGroups"/>), by season number.</summary>
    public IReadOnlyDictionary<int, string> SeasonGroups { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// Seasons whose off-group files a release from the season's group may replace at equal quality and score: an
    /// "Align season" search, or every season with a group when the automatic alignment is on (but those kept as they
    /// are). Empty otherwise: the group then only breaks ties between releases.
    /// </summary>
    public IReadOnlySet<int> AlignSeasons { get; init; } = new HashSet<int>();

    /// <summary>
    /// Shared episode ids the automatic alignment gave up on (the season's group never released them in time): their
    /// files are judged as ordinary files again, whatever <see cref="AlignSeasons"/> says.
    /// </summary>
    public IReadOnlySet<int> AlignGivenUp { get; init; } = new HashSet<int>();

    public string Describe() => Describe(Slots);

    public static string Describe(IReadOnlyCollection<EpisodeSlot> slots)
    {
        if (slots.Count == 0)
        {
            return "no episode";
        }

        var ordered = slots.OrderBy(s => s.SeasonNumber).ThenBy(s => s.EpisodeNumber).ToList();
        if (ordered.Count == 1)
        {
            return ordered[0].Label;
        }

        if (ordered.Count <= 4)
        {
            return string.Join(", ", ordered.Select(s => s.Label));
        }

        return $"{ordered[0].Label}–{ordered[^1].Label} ({ordered.Count} episodes)";
    }
}
