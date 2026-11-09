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
