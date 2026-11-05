using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Tags;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Movies;

/// <summary>
/// The library's bulk editor: one action on one version (or all) of many films. Each film follows the rules of the
/// single-item paths and publishes the same events; a film that lacks the version is skipped and reported.
/// </summary>
public sealed class BulkEditService(BigarrDbContext db, MovieService movies, OutboxWriter outbox, SearchQueue searches, ILogger<BulkEditService> logger)
{
    /// <summary>One save for the whole request: every version changes, or none.</summary>
    public async Task<Result<BulkResponse>> UpdateAsync(BulkUpdateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.MovieIds, request.Versions, out var selector) is { } invalid)
        {
            return invalid;
        }

        var tagsMode = request.ApplyTags ?? TagsMode.Add;
        var changesTags = request.TagIds is not null && (request.TagIds.Count > 0 || tagsMode == TagsMode.Replace);
        if (request.Monitored is null && request.QualityProfileId is null && request.MinimumAvailability is null && !changesTags)
        {
            return Failure.Invalid("Nothing to change", "Set monitored, a quality profile, a minimum availability or tags.");
        }

        var profile = request.QualityProfileId is { } profileId ? await db.QualityProfiles.FindAsync([profileId], ct) : null;
        if (request.QualityProfileId is not null && profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
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

        // Only the selected versions are loaded and tracked: the films themselves only lend their reference to the events.
        var ids = request.MovieIds.Distinct().ToList();
        var films = await db.Movies.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new MovieRef(m.Id, m.TmdbId, m.ImdbId, m.Title, m.Year))
            .ToDictionaryAsync(m => m.Id, ct);
        var versions = (await selector.Filter(db.MovieVersions.Where(v => ids.Contains(v.MovieId)))
                .Include(v => v.QualityProfile)
                .Include(v => v.RootFolder)
                .Include(v => v.Tags)
                .AsSplitQuery()
                .ToListAsync(ct))
            .ToLookup(v => v.MovieId);

        var results = new List<BulkMovieResult>();
        foreach (var id in ids)
        {
            if (!films.TryGetValue(id, out var movie))
            {
                results.Add(NotFound(id));
                continue;
            }

            var picked = versions[id].OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToList();
            if (picked.Count == 0)
            {
                results.Add(new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Skipped, [], selector.SkipReason));
                continue;
            }

            var changed = new List<string>();
            foreach (var version in picked)
            {
                if (Apply(version, request, profile, tagsMode, changesTags ? tags : null))
                {
                    // Same rule as the single PUT: consumers only hear about versions that actually changed.
                    outbox.Publish(new VersionUpdated(movie, VersionRef.From(version)));
                    changed.Add(version.Name);
                }
            }

            results.Add(changed.Count > 0
                ? new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Updated, changed)
                : new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Unchanged, picked.Select(v => v.Name).ToList()));
        }

        await db.SaveChangesAsync(ct);
        return Result<BulkResponse>.Ok(new BulkResponse(results));
    }

    /// <summary>Returns true when the version changed.</summary>
    private static bool Apply(MovieVersion version, BulkUpdateRequest request, QualityProfile? profile, TagsMode tagsMode, List<Tag>? tags)
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

        if (request.MinimumAvailability is { } availability && version.MinimumAvailability != availability)
        {
            version.MinimumAvailability = availability;
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
    /// Queues an automatic search per selected version, like the wanted page: the runner searches one version at a time
    /// and applies the usual rules (one active grab per version, blocklist, profile).
    /// </summary>
    public async Task<Result<BulkResponse>> SearchAsync(BulkSearchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.MovieIds, request.Versions, out var selector) is { } invalid)
        {
            return invalid;
        }

        var ids = request.MovieIds.Distinct().ToList();
        var found = await LoadPlanRowsAsync(ids, ct);
        var results = new List<BulkMovieResult>();
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var movie))
            {
                results.Add(NotFound(id));
                continue;
            }

            var picked = selector.Pick(movie.Versions, v => v.IsDefault, v => v.Name);
            if (picked.Count == 0)
            {
                results.Add(new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Skipped, [], selector.SkipReason));
                continue;
            }

            var queued = picked.Count(v => searches.TryEnqueue(v.Id, SearchTrigger.User));
            results.Add(new BulkMovieResult(movie.Id, movie.Title, queued > 0 ? BulkOutcome.Queued : BulkOutcome.AlreadyQueued, picked.Select(v => v.Name).ToList()));
        }

        return Result<BulkResponse>.Ok(new BulkResponse(results));
    }

    /// <summary>
    /// Deletes film by film through <see cref="MovieService"/>, since files move to the recycle bin as it goes: a film
    /// that fails is reported and the others carry on. A dry run returns the same plan and touches nothing.
    /// </summary>
    public async Task<Result<BulkDeleteResponse>> DeleteAsync(BulkDeleteRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.MovieIds, request.Versions, out var selector) is { } invalid)
        {
            return invalid;
        }

        var recycleBin = request.DeleteFiles
            ? await db.MediaManagement.AsNoTracking().Where(c => c.Id == MediaManagementConfig.SingletonId).Select(c => c.RecycleBinPath).FirstOrDefaultAsync(ct)
            : null;

        var ids = request.MovieIds.Distinct().ToList();
        var found = await LoadPlanRowsAsync(ids, ct);
        var plans = ids.Select(id => found.TryGetValue(id, out var movie) ? Plan(movie, selector, request.DeleteFiles) : new DeletePlan(NotFound(id), null, null)).ToList();
        if (request.DryRun)
        {
            return Result<BulkDeleteResponse>.Ok(new BulkDeleteResponse(true, recycleBin, plans.Select(p => p.Result).ToList()));
        }

        var results = new List<BulkMovieResult>();
        foreach (var plan in plans)
        {
            var result = plan.Result;
            if (result.Outcome is BulkOutcome.MovieDeleted or BulkOutcome.VersionsDeleted)
            {
                var done = plan.VersionId is { } versionId
                    ? (await movies.DeleteVersionAsync(result.MovieId, versionId, request.DeleteFiles, plan.Promote, request.AddImportExclusion, ct)).Failure
                    : (await movies.DeleteMovieAsync(result.MovieId, request.DeleteFiles, request.AddImportExclusion, ct)).Failure;
                if (done is not null)
                {
                    logger.LogWarning("Bulk delete of {Movie} failed: {Title} {Detail}", result.Title, done.Title, done.Detail);
                    result = result with { Outcome = BulkOutcome.Failed, Detail = done.Detail ?? done.Title, Files = 0, Promoted = null };
                }

                // A failed film may leave rows behind (journal lines written before the folder refused to move): none
                // of them may ride along with the next film's save.
                db.ChangeTracker.Clear();
            }

            results.Add(result);
        }

        return Result<BulkDeleteResponse>.Ok(new BulkDeleteResponse(false, recycleBin, results));
    }

    /// <summary>
    /// What deleting the selection does to one film. <c>all</c> or its only version takes the film; a default version
    /// hands over to the oldest managed sibling, like the compat DELETE; with none, the film is skipped.
    /// </summary>
    private static DeletePlan Plan(PlanMovie movie, VersionSelector selector, bool deleteFiles)
    {
        if (selector.IsAll)
        {
            return new DeletePlan(
                new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.MovieDeleted, movie.Versions.Select(v => v.Name).ToList(), Files: deleteFiles ? movie.Versions.Count(v => v.HasFile) : 0),
                null,
                null);
        }

        var version = selector.Pick(movie.Versions, v => v.IsDefault, v => v.Name).FirstOrDefault();
        if (version is null)
        {
            return new DeletePlan(new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Skipped, [], selector.SkipReason), null, null);
        }

        var files = deleteFiles && version.HasFile ? 1 : 0;
        var siblings = movie.Versions.Where(v => v.Id != version.Id).ToList();
        if (siblings.Count == 0)
        {
            return new DeletePlan(new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.MovieDeleted, [version.Name], Files: files), version.Id, null);
        }

        PlanVersion? successor = null;
        if (version.IsDefault)
        {
            successor = siblings.Where(v => v.Mode == VersionMode.Managed).OrderBy(v => v.Id).FirstOrDefault();
            if (successor is null)
            {
                return new DeletePlan(
                    new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.Skipped, [version.Name], "Default version and no other managed version can take over"),
                    null,
                    null);
            }
        }

        return new DeletePlan(new BulkMovieResult(movie.Id, movie.Title, BulkOutcome.VersionsDeleted, [version.Name], null, files, successor?.Name), version.Id, successor?.Id);
    }

    private async Task<Dictionary<int, PlanMovie>> LoadPlanRowsAsync(List<int> ids, CancellationToken ct) =>
        await db.Movies.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new PlanMovie(
                m.Id,
                m.Title,
                m.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).Select(v => new PlanVersion(v.Id, v.Name, v.Mode, v.IsDefault, v.File != null)).ToList()))
            .ToDictionaryAsync(m => m.Id, ct);

    private static Failure? Validate(List<int>? movieIds, string? versions, out VersionSelector selector)
    {
        if (!VersionSelector.TryParse(versions, out selector))
        {
            return Failure.Invalid("Invalid version selector", $"'{versions}': expected 'default', 'all' or 'version:<name>'.");
        }

        return movieIds is null || movieIds.Count == 0 ? Failure.Invalid("No movies", "Select at least one movie.") : null;
    }

    private static BulkMovieResult NotFound(int id) => new(id, $"#{id}", BulkOutcome.Skipped, [], "Not in the library");

    private sealed record PlanMovie(int Id, string Title, List<PlanVersion> Versions);

    private sealed record PlanVersion(int Id, string Name, VersionMode Mode, bool IsDefault, bool HasFile);

    /// <param name="VersionId">Null when the whole film goes (<c>all</c>).</param>
    private sealed record DeletePlan(BulkMovieResult Result, int? VersionId, int? Promote);
}
