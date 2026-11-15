namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Movies;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tags;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The series library's bulk editor, the film one's twin (<see cref="BulkEditService"/>): one action on one version (or
/// all) of many series, each following the single-item rules of <see cref="SeriesService"/>. A series that lacks the
/// version is skipped and reported.
/// </summary>
public sealed class SeriesBulkEditService(BigarrDbContext db, SeriesService series, Infrastructure.Events.OutboxWriter outbox, ILogger<SeriesBulkEditService> logger)
{
    /// <summary>One save for the whole request: every version changes, or none.</summary>
    public async Task<Result<SeriesBulkResponse>> UpdateAsync(SeriesBulkUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.SeriesIds, request.Versions, out var selector) is { } invalid)
        {
            return invalid;
        }

        var tagsMode = request.ApplyTags ?? TagsMode.Add;
        var changesTags = request.TagIds is not null && (request.TagIds.Count > 0 || tagsMode == TagsMode.Replace);
        if (request.Monitored is null && request.QualityProfileId is null && request.MonitorNewItems is null && request.SeriesType is null && !changesTags)
        {
            return Failure.Invalid("Nothing to change", "Set monitored, a quality profile, new-item monitoring, a series type or tags.");
        }

        var profile = request.QualityProfileId is { } profileId ? await db.QualityProfiles.FindAsync([profileId], ct) : null;
        if (request.QualityProfileId is not null && profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
        }

        if (Quality.ProfileKindRules.ForSeriesVersion(profile) is { } wrongKind)
        {
            return wrongKind;
        }

        var tags = new List<Tag>();
        if (changesTags)
        {
            var wanted = request.TagIds!.Distinct().ToList();
            tags = await db.Tags.Where(t => wanted.Contains(t.Id)).ToListAsync(ct);
            var unknown = wanted.Except(tags.Select(t => t.Id)).ToList();
            if (unknown.Count > 0)
            {
                return Failure.Invalid("Unknown tag", $"No tag with id {string.Join(", ", unknown)}.");
            }
        }

        var ids = request.SeriesIds.Distinct().ToList();
        var shows = await db.Series
            .Where(s => ids.Contains(s.Id))
            .Include(s => s.Versions).ThenInclude(v => v.Tags)
            .Include(s => s.Versions).ThenInclude(v => v.QualityProfile)
            .AsSplitQuery()
            .ToDictionaryAsync(s => s.Id, ct);

        var results = new List<BulkSeriesResult>();
        foreach (var id in ids)
        {
            if (!shows.TryGetValue(id, out var show))
            {
                results.Add(NotFound(id));
                continue;
            }

            // The series type belongs to the series: it changes even when the series lacks the version.
            var typeChanged = request.SeriesType is { } type && show.SeriesType != type;
            if (typeChanged)
            {
                show.SeriesType = request.SeriesType!.Value;
            }

            var picked = selector.Pick(show.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id), v => v.IsDefault, v => v.Name);
            var versionChange = request.Monitored is not null || profile is not null || request.MonitorNewItems is not null || changesTags;
            if (picked.Count == 0 && versionChange && !typeChanged)
            {
                results.Add(new BulkSeriesResult(show.Id, show.Title, request.SeriesType is null ? BulkOutcome.Skipped : BulkOutcome.Unchanged, [], request.SeriesType is null ? selector.SkipReason : null));
                continue;
            }

            var changedVersions = versionChange ? picked.Where(v => Apply(v, request, profile, tagsMode, changesTags ? tags : null)).ToList() : [];
            var changed = changedVersions.Select(v => v.Name).ToList();
            foreach (var version in changedVersions)
            {
                outbox.Publish(new Bigarr.Domain.Events.SeriesVersionUpdated(Bigarr.Domain.Events.SeriesRef.From(show), Bigarr.Domain.Events.SeriesVersionRef.From(version)));
            }

            var reported = versionChange ? picked.Select(v => v.Name).ToList() : [];
            results.Add(changed.Count > 0 || typeChanged
                ? new BulkSeriesResult(show.Id, show.Title, BulkOutcome.Updated, changed)
                : new BulkSeriesResult(show.Id, show.Title, BulkOutcome.Unchanged, reported));
        }

        await db.SaveChangesAsync(ct);
        return Result<SeriesBulkResponse>.Ok(new SeriesBulkResponse(results));
    }

    /// <summary>Returns true when the version changed.</summary>
    private static bool Apply(SeriesVersion version, SeriesBulkUpdateRequest request, QualityProfile? profile, TagsMode tagsMode, List<Tag>? tags)
    {
        var changed = false;
        if (request.Monitored is { } monitored && version.Monitored != monitored)
        {
            version.Monitored = monitored;
            changed = true;
        }

        if (profile is not null && version.QualityProfileId != profile.Id)
        {
            version.QualityProfile = profile;
            changed = true;
        }

        if (request.MonitorNewItems is { } newItems && version.MonitorNewItems != newItems)
        {
            version.MonitorNewItems = newItems;
            changed = true;
        }

        if (tags is not null)
        {
            var current = version.Tags.Select(t => t.Id).ToHashSet();
            var next = tagsMode switch
            {
                TagsMode.Remove => version.Tags.Where(t => tags.All(r => r.Id != t.Id)).ToList(),
                TagsMode.Replace => tags,
                _ => version.Tags.Concat(tags.Where(t => !current.Contains(t.Id))).ToList(),
            };
            if (!current.SetEquals(next.Select(t => t.Id)))
            {
                version.Tags.Clear();
                foreach (var tag in next)
                {
                    version.Tags.Add(tag);
                }

                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Deletes series by series through <see cref="SeriesService"/>, since files move to the recycle bin as it goes: a
    /// series that fails is reported and the others carry on. A dry run returns the same plan and touches nothing.
    /// </summary>
    public async Task<Result<SeriesBulkDeleteResponse>> DeleteAsync(SeriesBulkDeleteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.SeriesIds, request.Versions, out var selector) is { } invalid)
        {
            return invalid;
        }

        var recycleBin = request.DeleteFiles
            ? await db.MediaManagement.AsNoTracking().Where(c => c.Id == MediaManagementConfig.SingletonId).Select(c => c.RecycleBinPath).FirstOrDefaultAsync(ct)
            : null;

        var ids = request.SeriesIds.Distinct().ToList();
        var found = await db.Series.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new PlanSeries(
                s.Id,
                s.Title,
                s.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id)
                    .Select(v => new PlanVersion(v.Id, v.Name, v.Mode, v.IsDefault, db.EpisodeFiles.Count(f => f.VersionId == v.Id)))
                    .ToList()))
            .ToDictionaryAsync(s => s.Id, ct);
        var plans = ids.Select(id => found.TryGetValue(id, out var show) ? Plan(show, selector, request.DeleteFiles) : new DeletePlan(NotFound(id), null, null)).ToList();
        if (request.DryRun)
        {
            return Result<SeriesBulkDeleteResponse>.Ok(new SeriesBulkDeleteResponse(true, recycleBin, plans.Select(p => p.Result).ToList()));
        }

        var results = new List<BulkSeriesResult>();
        foreach (var plan in plans)
        {
            var result = plan.Result;
            if (result.Outcome is BulkOutcome.SeriesDeleted or BulkOutcome.VersionsDeleted)
            {
                var done = plan.VersionId is { } versionId
                    ? (await series.DeleteVersionAsync(result.SeriesId, versionId, plan.Promote, ct, request.DeleteFiles)).Failure
                    : (await series.DeleteSeriesAsync(result.SeriesId, ct, request.DeleteFiles)).Failure;
                if (done is not null)
                {
                    logger.LogWarning("Bulk delete of {Series} failed: {Title} {Detail}", result.Title, done.Title, done.Detail);
                    result = result with { Outcome = BulkOutcome.Failed, Detail = done.Detail ?? done.Title, Files = 0, Promoted = null };
                }

                // A failed series may leave tracked rows behind: none may ride along with the next one's save.
                db.ChangeTracker.Clear();
            }

            results.Add(result);
        }

        return Result<SeriesBulkDeleteResponse>.Ok(new SeriesBulkDeleteResponse(false, recycleBin, results));
    }

    /// <summary>
    /// The film rules: <c>all</c> or its only version takes the series; a default version hands over to the oldest managed
    /// sibling; with none, the series is skipped.
    /// </summary>
    private static DeletePlan Plan(PlanSeries show, VersionSelector selector, bool deleteFiles)
    {
        if (selector.IsAll)
        {
            return new DeletePlan(
                new BulkSeriesResult(show.Id, show.Title, BulkOutcome.SeriesDeleted, show.Versions.Select(v => v.Name).ToList(), Files: deleteFiles ? show.Versions.Sum(v => v.Files) : 0),
                null,
                null);
        }

        var version = selector.Pick(show.Versions, v => v.IsDefault, v => v.Name).FirstOrDefault();
        if (version is null)
        {
            return new DeletePlan(new BulkSeriesResult(show.Id, show.Title, BulkOutcome.Skipped, [], selector.SkipReason), null, null);
        }

        var files = deleteFiles ? version.Files : 0;
        var siblings = show.Versions.Where(v => v.Id != version.Id).ToList();
        if (siblings.Count == 0)
        {
            return new DeletePlan(new BulkSeriesResult(show.Id, show.Title, BulkOutcome.SeriesDeleted, [version.Name], Files: files), version.Id, null);
        }

        PlanVersion? successor = null;
        if (version.IsDefault)
        {
            successor = siblings.Where(v => v.Mode == VersionMode.Managed).OrderBy(v => v.Id).FirstOrDefault();
            if (successor is null)
            {
                return new DeletePlan(
                    new BulkSeriesResult(show.Id, show.Title, BulkOutcome.Skipped, [version.Name], "Default version and no other managed version can take over"),
                    null,
                    null);
            }
        }

        return new DeletePlan(new BulkSeriesResult(show.Id, show.Title, BulkOutcome.VersionsDeleted, [version.Name], null, files, successor?.Name), version.Id, successor?.Id);
    }

    private static Failure? Validate(List<int>? seriesIds, string? versions, out VersionSelector selector)
    {
        if (!VersionSelector.TryParse(versions, out selector))
        {
            return Failure.Invalid("Invalid version selector", $"'{versions}': expected 'default', 'all' or 'version:<name>'.");
        }

        return seriesIds is null || seriesIds.Count == 0 ? Failure.Invalid("No series", "Select at least one series.") : null;
    }

    private static BulkSeriesResult NotFound(int id) => new(id, $"#{id}", BulkOutcome.Skipped, [], "Not in the library");

    private sealed record PlanSeries(int Id, string Title, List<PlanVersion> Versions);

    private sealed record PlanVersion(int Id, string Name, VersionMode Mode, bool IsDefault, int Files);

    /// <param name="VersionId">Null when the whole series goes (<c>all</c>).</param>
    private sealed record DeletePlan(BulkSeriesResult Result, int? VersionId, int? Promote);
}
