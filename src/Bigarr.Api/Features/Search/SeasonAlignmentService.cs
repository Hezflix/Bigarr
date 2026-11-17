using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Decision;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Search;

/// <param name="Files">Files of the group in the season (a shadow pick counts once per episode).</param>
public sealed record SeasonGroupCount(string Group, int Files);

/// <summary>An episode whose file (or pick) is from another group than the season's: what an alignment replaces.</summary>
public sealed record SeasonAlignmentEpisode(int EpisodeId, string Label, string? Title, string? CurrentGroup, string Quality, bool IsPick);

/// <summary>
/// The dry run of "Align season": the season's group, how it was found, and the episodes a search would try to replace
/// with that group's release. Nothing is searched or grabbed.
/// </summary>
/// <param name="SeasonGroup">Null when the season has none; <paramref name="Reason"/> then says why.</param>
public sealed record SeasonAlignmentResponse(
    int SeriesId,
    int VersionId,
    int SeasonNumber,
    string? SeasonGroup,
    string? Reason,
    int MinimumFiles,
    List<SeasonGroupCount> Groups,
    List<SeasonAlignmentEpisode> Episodes);

/// <param name="Search">The search run for the off-group episodes; null when there was nothing to align.</param>
public sealed record SeasonAlignmentResult(SeasonAlignmentResponse Preview, SeriesAutomaticSearchResult? Search);

/// <summary>
/// "Align season": one release group per season, as Sonarr users keep it by hand. The preview names the season's group
/// (<see cref="SeasonGroups"/>) and the episodes from another group; aligning searches those episodes (as a season when
/// there are several, so the group's season pack is welcome) and takes only releases of the season's group, at a
/// quality and custom format score no lower than the file in place.
/// </summary>
public sealed class SeasonAlignmentService(SeriesTargetsFactory targetsFactory, SeriesAutomaticSearchService search)
{
    public async Task<SeasonAlignmentResponse?> PreviewAsync(int seriesId, int versionId, int seasonNumber, CancellationToken ct)
    {
        var targets = await targetsFactory.LoadAsync(seriesId, ct);
        return targets is null ? null : Preview(targets, versionId, seasonNumber);
    }

    /// <summary>Null when the series or the version is unknown.</summary>
    public static SeasonAlignmentResponse? Preview(SeriesTargets targets, int versionId, int seasonNumber)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Series.Versions.All(v => v.Id != versionId))
        {
            return null;
        }

        var slots = targets.SeasonSlots(versionId, seasonNumber);
        var counts = SeasonGroups.Count(SeriesTargets.FileGroups(slots));
        var group = targets.SeasonGroupsOf(versionId).GetValueOrDefault(seasonNumber);
        var episodes = new List<SeasonAlignmentEpisode>();
        if (group is not null)
        {
            var titles = targets.Episodes.ToDictionary(e => e.Id, e => e.Title);
            episodes = slots
                .Where(s => s.File is { } file && !SeasonGroups.Same(file.ReleaseGroup, group))
                .Select(s => new SeasonAlignmentEpisode(
                    s.EpisodeId,
                    s.Label,
                    titles.GetValueOrDefault(s.EpisodeId),
                    string.IsNullOrWhiteSpace(s.File!.ReleaseGroup) ? null : s.File.ReleaseGroup,
                    targets.QualityName(s.File.QualityId),
                    s.File.IsPick))
                .ToList();
        }

        return new SeasonAlignmentResponse(
            targets.Series.Id,
            versionId,
            seasonNumber,
            group,
            group is null ? SeasonGroups.WhyNone(seasonNumber, counts) : null,
            SeasonGroups.MinimumFiles,
            counts.Select(c => new SeasonGroupCount(c.Group, c.Files)).ToList(),
            episodes);
    }

    /// <summary>Searches the off-group episodes of the season for its group's release; null when the series or version is unknown.</summary>
    public async Task<SeasonAlignmentResult?> AlignAsync(int seriesId, int versionId, int seasonNumber, CancellationToken ct)
    {
        var preview = await PreviewAsync(seriesId, versionId, seasonNumber, ct);
        if (preview is null)
        {
            return null;
        }

        if (preview.SeasonGroup is null || preview.Episodes.Count == 0)
        {
            return new SeasonAlignmentResult(preview, null);
        }

        var result = await search.SearchEpisodesAsync(versionId, preview.Episodes.Select(e => e.EpisodeId).ToList(), SearchTrigger.User, ct, new HashSet<int> { seasonNumber });
        return new SeasonAlignmentResult(preview, result);
    }
}
