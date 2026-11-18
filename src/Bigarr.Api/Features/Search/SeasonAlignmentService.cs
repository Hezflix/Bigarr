using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Decision;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Search;

/// <param name="Files">Files of the group in the season (a shadow pick counts once per episode).</param>
public sealed record SeasonGroupCount(string Group, int Files);

/// <summary>An episode whose file (or pick) is from another group than the season's: what an alignment replaces.</summary>
/// <param name="GivesUpAt">With the automatic alignment on (and the season not kept): when it stops looking for this one.</param>
/// <param name="GaveUp">The automatic alignment no longer looks for it; "Align season" still does.</param>
public sealed record SeasonAlignmentEpisode(int EpisodeId, string Label, string? Title, string? CurrentGroup, string Quality, bool IsPick, DateTime? GivesUpAt = null, bool GaveUp = false);

/// <summary>
/// The dry run of "Align season": the season's group, how it was found, and the episodes a search would try to replace
/// with that group's release. Nothing is searched or grabbed.
/// </summary>
/// <param name="SeasonGroup">The group the season aligns to (the pinned one, else the one with the most files); null when the season has none, <paramref name="Reason"/> then says why.</param>
public sealed record SeasonAlignmentResponse(
    int SeriesId,
    int VersionId,
    int SeasonNumber,
    string? SeasonGroup,
    string? Reason,
    int MinimumFiles,
    List<SeasonGroupCount> Groups,
    List<SeasonAlignmentEpisode> Episodes,
    /// <summary>The automatic alignment setting (Settings → Media management).</summary>
    bool AutoAlign = false,
    /// <summary>"Keep as is": the automatic alignment leaves this season alone.</summary>
    bool KeepAsIs = false,
    /// <summary>The group with the most files (at least 3, no tie), pins aside; null without one.</summary>
    string? MajorityGroup = null,
    /// <summary>The group the user chose for the season, when set: it wins over <paramref name="MajorityGroup"/>.</summary>
    string? PinnedGroup = null);

/// <summary>"Keep as is" on or off for a season of a version.</summary>
public sealed record SeasonAlignmentKeepRequest(bool KeepAsIs);

/// <summary>Pins the season to a release group present in it; null (or the group with the most files) goes back to following the files.</summary>
public sealed record SeasonAlignmentGroupRequest(string? Group);

/// <param name="Search">The search run for the off-group episodes; null when there was nothing to align.</param>
public sealed record SeasonAlignmentResult(SeasonAlignmentResponse Preview, SeriesAutomaticSearchResult? Search);

/// <summary>
/// "Align season": one release group per season, as Sonarr users keep it by hand. The preview names the season's group
/// (<see cref="SeasonGroups"/>) and the episodes from another group; aligning searches those episodes (as a season when
/// there are several, so the group's season pack is welcome) and takes only releases of the season's group, at a
/// quality and custom format score no lower than the file in place.
/// </summary>
public sealed class SeasonAlignmentService(BigarrDbContext db, SeriesTargetsFactory targetsFactory, SeriesAutomaticSearchService search)
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
        var keep = targets.KeepsReleaseGroups(versionId, seasonNumber);
        var auto = targets.AutoAlignSeasons(versionId).Contains(seasonNumber);
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
                    s.File.IsPick,
                    auto ? targets.AlignmentGivesUpAt(s) : null,
                    auto && targets.AlignmentGaveUp(s)))
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
            episodes,
            targets.AutoAlignEnabled,
            keep,
            targets.MajorityGroupsOf(versionId).GetValueOrDefault(seasonNumber),
            targets.PinnedGroupsOf(versionId).GetValueOrDefault(seasonNumber));
    }

    /// <summary>Sets "keep as is" on a season of a version; null when the series, version or season is unknown.</summary>
    public async Task<SeasonAlignmentResponse?> KeepAsync(int seriesId, int versionId, int seasonNumber, bool keep, CancellationToken ct)
    {
        var row = await db.SeriesVersionSeasons
            .FirstOrDefaultAsync(s => s.VersionId == versionId && s.Version.SeriesId == seriesId && s.Season.SeasonNumber == seasonNumber, ct);
        if (row is null)
        {
            return null;
        }

        row.KeepReleaseGroups = keep;
        await db.SaveChangesAsync(ct);
        return await PreviewAsync(seriesId, versionId, seasonNumber, ct);
    }

    /// <summary>
    /// Pins the season of the version to <paramref name="group"/>, one of the groups of its files (any number of them);
    /// null, or the group with the most files, unpins it. Specials never take a group.
    /// </summary>
    public async Task<Result<SeasonAlignmentResponse>?> PinAsync(int seriesId, int versionId, int seasonNumber, string? group, CancellationToken ct)
    {
        var row = await db.SeriesVersionSeasons
            .FirstOrDefaultAsync(s => s.VersionId == versionId && s.Version.SeriesId == seriesId && s.Season.SeasonNumber == seasonNumber, ct);
        var targets = row is null ? null : await targetsFactory.LoadAsync(seriesId, ct);
        if (row is null || targets is null)
        {
            return null;
        }

        string? pinned = null;
        if (!string.IsNullOrWhiteSpace(group))
        {
            if (seasonNumber <= 0)
            {
                return Failure.Invalid("Specials take no release group", "Each special keeps the group it comes from.");
            }

            var present = SeasonGroups.Count(SeriesTargets.FileGroups(targets.SeasonSlots(versionId, seasonNumber))).Select(c => c.Group).ToList();
            pinned = present.FirstOrDefault(g => SeasonGroups.Same(g, group));
            if (pinned is null)
            {
                return Failure.Invalid("Unknown release group", $"No file of the season comes from {group.Trim()}.");
            }

            // Choosing the group with the most files is following the files: nothing to pin.
            if (SeasonGroups.Same(targets.MajorityGroupsOf(versionId).GetValueOrDefault(seasonNumber), pinned))
            {
                pinned = null;
            }
        }

        row.PinnedReleaseGroup = pinned;
        await db.SaveChangesAsync(ct);
        return Result<SeasonAlignmentResponse>.Ok((await PreviewAsync(seriesId, versionId, seasonNumber, ct))!);
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

/// <summary>
/// The season groups of the files on disk (or the pinned ones), per (version, season), with how many episodes have a file from another
/// group: the series page's summary and the Wanted list read it without loading every series graph.
/// </summary>
public static class SeasonGroupIndex
{
    public static async Task<Dictionary<(int VersionId, int SeasonNumber), (string? Group, int OffGroup)>> LoadAsync(BigarrDbContext db, IReadOnlyCollection<int>? versionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.EpisodeTargets.AsNoTracking()
            .Where(t => (versionIds == null || versionIds.Contains(t.VersionId)) && t.EpisodeFileId != null && t.Episode.SeasonNumber > 0)
            .Select(t => new { t.VersionId, t.Episode.SeasonNumber, FileId = t.EpisodeFileId!.Value, t.EpisodeFile!.ReleaseGroup })
            .ToListAsync(ct);
        var pins = (await db.SeriesVersionSeasons.AsNoTracking()
                .Where(s => (versionIds == null || versionIds.Contains(s.VersionId)) && s.PinnedReleaseGroup != null && s.Season.SeasonNumber > 0)
                .Select(s => new { s.VersionId, s.Season.SeasonNumber, s.PinnedReleaseGroup })
                .ToListAsync(ct))
            .ToDictionary(p => (p.VersionId, p.SeasonNumber), p => p.PinnedReleaseGroup!);
        var result = new Dictionary<(int, int), (string?, int)>();
        foreach (var pin in pins)
        {
            result[pin.Key] = (pin.Value, 0);
        }

        foreach (var season in rows.GroupBy(r => (r.VersionId, r.SeasonNumber)))
        {
            var group = pins.GetValueOrDefault(season.Key) ?? SeasonGroups.Of(season.Key.SeasonNumber, season.DistinctBy(r => r.FileId).Select(r => r.ReleaseGroup));
            if (group is not null)
            {
                result[season.Key] = (group, season.Count(r => !SeasonGroups.Same(r.ReleaseGroup, group)));
            }
        }

        return result;
    }

    /// <summary>
    /// Seasons (per version) whose files come from several known release groups: there is a group to choose even when
    /// none has the most files, so the series page offers "Align season" there too.
    /// </summary>
    public static async Task<HashSet<(int VersionId, int SeasonNumber)>> MixedAsync(BigarrDbContext db, IReadOnlyCollection<int> versionIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var rows = await db.EpisodeFiles.AsNoTracking()
            .Where(f => versionIds.Contains(f.VersionId) && f.SeasonNumber > 0 && f.ReleaseGroup != null && f.ReleaseGroup != "")
            .Select(f => new { f.VersionId, f.SeasonNumber, f.ReleaseGroup })
            .Distinct()
            .ToListAsync(ct);
        return rows.GroupBy(r => (r.VersionId, r.SeasonNumber))
            .Where(g => g.Select(r => r.ReleaseGroup!.Trim()).Distinct(SeasonGroups.Comparer).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
    }
}
