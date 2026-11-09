using Bigarr.Parsing.Qualities;

namespace Bigarr.Decision.Specifications;

// Sonarr's episode-only rules (NzbDrone.Core.DecisionEngine.Specifications, develop). Each one is silent for a film
// release: they read the parsed episode name and the episodes the release covers for the version.

/// <summary>
/// What a search asked for (Sonarr's <c>EpisodeRequestedSpecification</c>, <c>SeasonMatchSpecification</c> and
/// <c>SingleEpisodeSearchMatchSpecification</c>): an episode search wants that episode and no pack, a season search
/// that season, a series search anything of the series. Matched on the episodes the release maps to, so scene and
/// absolute numbering are already resolved.
/// </summary>
public sealed class EpisodeRequestedSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (candidate.Search is not { } search || target.Episodes is not { } episodes || search.Kind == EpisodeSearchKind.Series)
        {
            return null;
        }

        var covered = episodes.Slots;
        if (search.Kind == EpisodeSearchKind.Season)
        {
            return covered.Any(s => search.EpisodeIds.Contains(s.EpisodeId))
                ? null
                : new Rejection($"Wrong season: the release is {EpisodeCoverage.Describe(covered)}");
        }

        if (candidate.ParsedEpisode?.FullSeason == true)
        {
            return new Rejection("Full season pack: an episode search wants the episode alone");
        }

        return covered.Any(s => search.EpisodeIds.Contains(s.EpisodeId))
            ? null
            : new Rejection($"Episode wasn't requested: the release is {EpisodeCoverage.Describe(covered)}{(search.Label is null ? string.Empty : $", not {search.Label}")}");
    }
}

/// <summary>Sonarr's <c>FullSeasonSpecification</c>: a season pack only once every episode of it has aired (24 h of slack).</summary>
public sealed class FullSeasonSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (candidate.ParsedEpisode?.FullSeason != true || target.Episodes is not { } episodes)
        {
            return null;
        }

        var limit = episodes.Now.AddHours(24);
        var notAired = episodes.Slots.Where(s => s.AirDateUtc is null || s.AirDateUtc.Value > limit).ToList();
        if (notAired.Count == 0)
        {
            return null;
        }

        var first = notAired.OrderBy(s => s.SeasonNumber).ThenBy(s => s.EpisodeNumber).First();
        var when = first.AirDateUtc is { } date ? $"airs {date:yyyy-MM-dd}" : "has no air date";
        return new Rejection($"Full season release rejected: not every episode has aired yet ({first.Label} {when}{(notAired.Count > 1 ? $", {notAired.Count} episodes in all" : string.Empty)})");
    }
}

/// <summary>Sonarr's <c>MultiSeasonSpecification</c>: never grabbed automatically; the interactive search can still grab it by hand.</summary>
public sealed class MultiSeasonSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        candidate.ParsedEpisode?.IsMultiSeason == true
            ? new Rejection("Multi-season release, not grabbed automatically: grab it from the interactive search to get every season")
            : null;
}

/// <summary>Sonarr's <c>SplitEpisodeSpecification</c>.</summary>
public sealed class SplitEpisodeSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        candidate.ParsedEpisode?.IsSplitEpisode == true ? new Rejection("Split episode releases are not supported") : null;
}

/// <summary>Sonarr's <c>NotSampleSpecification</c>: "sample" in the title and under 70 MB.</summary>
public sealed class NotSampleSpecification : IReleaseSpecification
{
    private const long SampleMaxSize = 70L * 1024 * 1024;

    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target) =>
        candidate.IsEpisode && candidate.Release.Title.Contains("sample", StringComparison.OrdinalIgnoreCase) && candidate.Release.Size < SampleMaxSize
            ? new Rejection("Sample")
            : null;
}

/// <summary>
/// Sonarr's <c>SameEpisodesGrabSpecification</c>: a file in place that holds more episodes than the release would be
/// broken up by it (an S01E01E02 file replaced by an S01E01 release).
/// </summary>
public sealed class SameEpisodesGrabSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Episodes is not { } episodes)
        {
            return null;
        }

        var covered = episodes.Slots.Select(s => s.EpisodeId).ToHashSet();
        return episodes.Slots.Any(s => s.File is { IsPick: false } && s.FileEpisodeIds.Any(id => !covered.Contains(id)))
            ? new Rejection("Episode file on disk contains more episodes than this release contains")
            : null;
    }
}

/// <summary>
/// Sonarr's <c>AnimeVersionUpgradeSpecification</c>: for an anime, a v2 of an episode in place is only an upgrade when it
/// comes from the group that made the file (Sonarr's default "prefer and upgrade" propers setting).
/// </summary>
public sealed class AnimeVersionUpgradeSpecification : IReleaseSpecification
{
    public Rejection? Evaluate(ReleaseCandidate candidate, TargetContext target)
    {
        if (target.Episodes is not { IsAnime: true } episodes || candidate.Quality?.Quality is null)
        {
            return null;
        }

        var group = candidate.ReleaseGroup;
        foreach (var slot in episodes.Slots)
        {
            if (slot.File is not { } file || !IsRevisionUpgrade(file, candidate.Quality))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(file.ReleaseGroup))
            {
                return new Rejection($"{slot.Label}: existing release group is unknown, a new version cannot be matched to it");
            }

            if (string.IsNullOrWhiteSpace(group))
            {
                return new Rejection("Release group is unknown, a new version cannot be matched to the existing file");
            }

            if (!string.Equals(file.ReleaseGroup, group, StringComparison.Ordinal))
            {
                return new Rejection($"{group} does not match existing release group {file.ReleaseGroup} ({slot.Label})");
            }
        }

        return null;
    }

    /// <summary>Sonarr's <c>IsRevisionUpgrade</c>: same quality, higher revision.</summary>
    private static bool IsRevisionUpgrade(ExistingFile file, QualityModel release) =>
        release.Quality.Id == file.QualityId
        && (release.Revision ?? new Revision()).CompareTo(new Revision(file.RevisionVersion, file.RevisionReal, file.IsRepack)) > 0;
}
