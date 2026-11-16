using System.Security.Claims;
using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Issues;

/// <summary>
/// What an approver runs from an issue, through the library's own paths: search the film, episode, season or series
/// again; blocklist the release that produced the file (and delete it) then search; delete the file. Each run leaves a
/// line in the thread: an outcome every reader may see, and the release names and paths for the admin.
/// <para>
/// While shadow mode is on, or while a followed Radarr/Sonarr carries the issue's version, the files are that app's:
/// Bigarr never deletes nor blocklists them (its library folders are not its to write). The search stays, and records a
/// pick in shadow mode like every other search. Nothing is sent to the followed app: the fix is made there.
/// </para>
/// </summary>
public sealed class IssueActionService(
    BigarrDbContext db,
    IssueFiles files,
    AutomaticSearchService movieSearch,
    SeriesAutomaticSearchService seriesSearch,
    BlocklistService blocklist,
    MovieService movies,
    SeriesManualImportService seriesFiles,
    IOptions<ShadowOptions> shadow,
    TimeProvider time,
    ILogger<IssueActionService> logger)
{
    /// <summary>The shadow sentinel of <see cref="IssueActionsResponse.ManagedBy"/>.</summary>
    public const string Shadow = "shadow";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Who owns the issue's files when it is not Bigarr: shadow mode, or the followed app carrying its version.</summary>
    public async Task<string?> ManagedByAsync(Issue issue, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (shadow.Value.Enabled)
        {
            return Shadow;
        }

        var name = issue.VersionName;
        return issue.MediaType == RequestMediaType.Movie
            ? await db.FollowedRadarrs.AsNoTracking().Where(r => r.Enabled && r.VersionName == name).Select(r => r.Name).FirstOrDefaultAsync(ct)
            : await db.FollowedSonarrs.AsNoTracking().Where(r => r.Enabled && r.VersionName == name).Select(r => r.Name).FirstOrDefaultAsync(ct);
    }

    /// <summary>What may be run now. The library title or its version gone: nothing.</summary>
    public async Task<IssueActionsResponse> AvailableAsync(Issue issue, CancellationToken ct)
    {
        var managedBy = await ManagedByAsync(issue, ct);
        if (!await InLibraryAsync(issue, ct))
        {
            return new IssueActionsResponse(false, false, false, managedBy);
        }

        var file = await files.CurrentAsync(issue, ct);
        var owned = managedBy is null;
        return new IssueActionsResponse(
            true,
            owned && file is { Snapshot.ReleaseTitle: not null },
            owned && file is not null,
            managedBy);
    }

    public async Task<Result<Issue>> SearchAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var issue = await LoadAsync(id, ct);
        if (!issue.IsSuccess)
        {
            return issue;
        }

        var search = await RunSearchAsync(issue.Value!, ct);
        return await LogAsync(principal, issue.Value!, IssueAction.Search, search.Outcome, search.Detail, ct);
    }

    public async Task<Result<Issue>> BlocklistAsync(ClaimsPrincipal principal, int id, bool deleteFile, CancellationToken ct)
    {
        var loaded = await LoadAsync(id, ct);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        var issue = loaded.Value!;
        var owned = await OwnedFileAsync(issue, ct);
        if (!owned.IsSuccess)
        {
            return owned.Failure!;
        }

        var file = owned.Value!;
        if (file.Snapshot.ReleaseTitle is not { } release)
        {
            return Failure.Conflict("Release unknown", "The file came from a scan or an import: no release to blocklist. Delete the file instead.");
        }

        // The grab that produced the file when Bigarr grabbed it; otherwise a stand-in carrying the release title, which
        // the decision engine matches the same way.
        var grab = file.Snapshot.GrabId is { } grabId ? await db.Grabs.AsNoTracking().Include(g => g.Targets).FirstOrDefaultAsync(g => g.Id == grabId, ct) : null;
        if (grab is null)
        {
            grab = new Grab { ReleaseTitle = release, IndexerName = file.Snapshot.IndexerName };
            foreach (var target in file.TargetIds)
            {
                grab.Targets.Add(new GrabTarget { TargetId = target });
            }
        }

        await blocklist.BlockAsync(grab, $"Reported in issue #{issue.Id}", Now, ct);
        await db.SaveChangesAsync(ct);
        var outcome = "The release was blocklisted.";
        var detail = $"Blocklisted {release}.";
        if (deleteFile)
        {
            var deleted = await DeleteAsync(issue, ct);
            if (deleted.Failure is { } failure)
            {
                return await LogAsync(principal, issue, IssueAction.BlocklistAndSearch, $"{outcome} The file could not be deleted.", $"{detail} {failure.Title}: {failure.Detail}", ct);
            }

            outcome += " The file was deleted.";
            detail += $" Deleted {file.Snapshot.RelativePath}.";
        }

        var search = await RunSearchAsync(issue, ct);
        return await LogAsync(principal, issue, IssueAction.BlocklistAndSearch, $"{outcome} {search.Outcome}", $"{detail} {search.Detail}", ct);
    }

    public async Task<Result<Issue>> DeleteFileAsync(ClaimsPrincipal principal, int id, bool search, CancellationToken ct)
    {
        var loaded = await LoadAsync(id, ct);
        if (!loaded.IsSuccess)
        {
            return loaded;
        }

        var issue = loaded.Value!;
        var owned = await OwnedFileAsync(issue, ct);
        if (!owned.IsSuccess)
        {
            return owned.Failure!;
        }

        var deleted = await DeleteAsync(issue, ct);
        if (deleted.Failure is { } failure)
        {
            return failure;
        }

        var outcome = "The file was deleted.";
        var detail = $"Deleted {owned.Value!.Snapshot.RelativePath}.";
        if (search)
        {
            var searched = await RunSearchAsync(issue, ct);
            (outcome, detail) = ($"{outcome} {searched.Outcome}", $"{detail} {searched.Detail}");
        }

        return await LogAsync(principal, issue, IssueAction.DeleteFile, outcome, detail, ct);
    }

    private async Task<Result<Issue>> LoadAsync(int id, CancellationToken ct)
    {
        var issue = await db.Issues.Include(i => i.ReportedBy).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return Failure.NotFound("Issue");
        }

        return await InLibraryAsync(issue, ct)
            ? Result<Issue>.Ok(issue)
            : Failure.Conflict("Not in the library any more", "The title or its version left the library: nothing to act on.");
    }

    private async Task<bool> InLibraryAsync(Issue issue, CancellationToken ct) => issue.MediaType == RequestMediaType.Movie
        ? issue.MovieVersionId is { } movieVersion && await db.MovieVersions.AnyAsync(v => v.Id == movieVersion, ct)
        : issue.SeriesVersionId is { } seriesVersion && await db.SeriesVersions.AnyAsync(v => v.Id == seriesVersion, ct)
            && (issue.Scope != IssueScope.Episode || (issue.EpisodeId is { } episode && await db.Episodes.AnyAsync(e => e.Id == episode, ct)));

    /// <summary>The issue's one file, when Bigarr owns it: a failure says why not (another app's, a season, no file).</summary>
    private async Task<Result<IssueFile>> OwnedFileAsync(Issue issue, CancellationToken ct)
    {
        if (await ManagedByAsync(issue, ct) is { } managedBy)
        {
            return Failure.Conflict(
                "The file is not Bigarr's",
                managedBy == Shadow
                    ? "Shadow mode is on: the files belong to the followed Radarr/Sonarr. Fix it there; searching from here only records a pick."
                    : $"{managedBy} manages this version's files: fix it there. Searching from here stays possible.");
        }

        if (issue.Scope is not (IssueScope.Movie or IssueScope.Episode))
        {
            return Failure.Conflict("No single file", "The issue is about a season or a whole series: search again, or act from the library.");
        }

        var file = await files.CurrentAsync(issue, ct);
        return file is null ? Failure.Conflict("No file", "There is no file now.") : Result<IssueFile>.Ok(file);
    }

    private async Task<Result<bool>> DeleteAsync(Issue issue, CancellationToken ct)
    {
        if (issue.Scope == IssueScope.Movie)
        {
            var result = await movies.DeleteFileAsync(issue.MovieId!.Value, issue.MovieVersionId!.Value, ct);
            return result.IsSuccess ? Result<bool>.Ok(true) : result.Failure!;
        }

        var file = await files.CurrentAsync(issue, ct);
        return file?.Snapshot.EpisodeFileId is { } fileId
            ? await seriesFiles.DeleteEpisodeFileAsync(issue.SeriesId!.Value, fileId, ct)
            : Failure.Conflict("No file", "There is no file now.");
    }

    private sealed record SearchRun(string Outcome, string Detail);

    private async Task<SearchRun> RunSearchAsync(Issue issue, CancellationToken ct)
    {
        try
        {
            if (issue.MediaType == RequestMediaType.Movie)
            {
                var result = (await movieSearch.SearchAsync([issue.MovieVersionId!.Value], SearchTrigger.User, ct)).FirstOrDefault();
                return result is null
                    ? new SearchRun("Nothing was searched.", "The version was not found.")
                    : new SearchRun(MovieOutcome(result.Outcome), result.ReleaseTitle ?? result.Detail ?? string.Empty);
            }

            var episodes = issue.Scope switch
            {
                IssueScope.Episode => [issue.EpisodeId!.Value],
                IssueScope.Season => await db.Episodes.Where(e => e.SeriesId == issue.SeriesId && e.SeasonNumber == issue.SeasonNumber && e.MovieLink == null).Select(e => e.Id).ToListAsync(ct),
                _ => await db.Episodes.Where(e => e.SeriesId == issue.SeriesId && e.MovieLink == null).Select(e => e.Id).ToListAsync(ct),
            };
            var series = await seriesSearch.SearchEpisodesAsync(issue.SeriesVersionId!.Value, episodes, SearchTrigger.User, ct);
            var outcome = series switch
            {
                { Grabbed: > 0 } => series.Grabbed == 1 ? "Searched again: a release was grabbed." : $"Searched again: {series.Grabbed} releases were grabbed.",
                { Picked: > 0 } => "Searched again: a release was picked (shadow mode, nothing was downloaded).",
                _ => "Searched again: nothing suitable was found.",
            };
            return new SearchRun(outcome, $"{series.Releases} releases seen, {series.Covered} of {series.Wanted} episodes covered. {series.Detail}".Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Search from issue {Id} failed", issue.Id);
            return new SearchRun("The search failed.", ex.Message);
        }
    }

    private static string MovieOutcome(AutomaticSearchOutcome outcome) => outcome switch
    {
        AutomaticSearchOutcome.Grabbed => "Searched again: a release was grabbed.",
        AutomaticSearchOutcome.Picked => "Searched again: a release was picked (shadow mode, nothing was downloaded).",
        AutomaticSearchOutcome.NoIndexer => "Searched again: no indexer to search.",
        AutomaticSearchOutcome.Skipped => "Searched again: a download is already under way.",
        AutomaticSearchOutcome.Failed => "Searched again: a release was found but no download client took it.",
        _ => "Searched again: nothing suitable was found.",
    };

    private async Task<Result<Issue>> LogAsync(ClaimsPrincipal principal, Issue issue, IssueAction action, string outcome, string detail, CancellationToken ct)
    {
        var now = Now;
        db.IssueComments.Add(new IssueComment
        {
            IssueId = issue.Id,
            Kind = IssueCommentKind.Action,
            AuthorId = principal.UserId(),
            Action = action,
            Outcome = Truncate(outcome, 512),
            Detail = Truncate(detail.Trim(), 4096),
            CreatedAt = now,
        });
        issue.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Result<Issue>.Ok(issue);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
