using System.Security.Claims;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Issues;

/// <summary>
/// Issues. A user with "report issues" reports on a title of the library; the reporter and the approvers (admin, request
/// manager) read the thread, comment, resolve and reopen; approvers see everyone's and run the actions. The file, the
/// release and the indexer are the admin's only.
/// </summary>
public static class IssuesEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapIssues(this IEndpointRouteBuilder app)
    {
        var signedIn = app.MapGroup("/api/v1/issues").WithTags("Issues").RequireAuthorization(AuthPolicies.SignedIn);
        signedIn.MapGet("/", ListAsync);
        signedIn.MapGet("/title", TitleAsync);
        signedIn.MapGet("/{id:int}", GetAsync);
        signedIn.MapPost("/{id:int}/comments", CommentAsync);
        signedIn.MapPut("/{id:int}/comments/{commentId:int}", EditCommentAsync);
        signedIn.MapDelete("/{id:int}/comments/{commentId:int}", DeleteCommentAsync);
        signedIn.MapPost("/{id:int}/resolve", ResolveAsync);
        signedIn.MapPost("/{id:int}/reopen", ReopenAsync);

        app.MapPost("/api/v1/issues", CreateAsync).WithTags("Issues").RequireAuthorization(AuthPolicies.Has(UserPermission.ReportIssues));

        var managers = app.MapGroup("/api/v1/issues").WithTags("Issues").RequireAuthorization(AuthPolicies.ManageRequests);
        managers.MapGet("/count", CountAsync);
        managers.MapPost("/{id:int}/search", SearchAsync);
        managers.MapPost("/{id:int}/blocklist", BlocklistAsync);
        managers.MapPost("/{id:int}/delete-file", DeleteFileAsync);
        return app;
    }

    private static IQueryable<Issue> Rows(BigarrDbContext db) =>
        db.Issues.AsNoTracking().Include(i => i.ReportedBy).Include(i => i.ResolvedBy);

    /// <param name="status">open, resolved, or all (default).</param>
    /// <param name="type">video, audio, subtitles or other; every type when absent.</param>
    /// <param name="q">Part of the title.</param>
    /// <param name="movieId">The admin's: issues on that library film.</param>
    /// <param name="seriesId">The admin's: issues on that library series.</param>
    /// <param name="mine">An approver's own issues only.</param>
    private static async Task<Ok<IssuePage>> ListAsync(
        ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, CancellationToken ct,
        string? status = null, string? type = null, string? q = null, int? movieId = null, int? seriesId = null, bool mine = false, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = Rows(db);
        if (!IssueService.Approver(principal) || mine)
        {
            var me = principal.UserId();
            query = query.Where(i => i.ReportedById == me);
        }

        if (principal.IsAdmin() && movieId is not null)
        {
            query = query.Where(i => i.MovieId == movieId);
        }

        if (principal.IsAdmin() && seriesId is not null)
        {
            query = query.Where(i => i.SeriesId == seriesId);
        }

        query = (status ?? "all").ToLowerInvariant() switch
        {
            "open" => query.Where(i => i.Status == IssueStatus.Open),
            "resolved" => query.Where(i => i.Status == IssueStatus.Resolved),
            _ => query,
        };

        if (Enum.TryParse<IssueType>(type, ignoreCase: true, out var issueType) && Enum.IsDefined(issueType))
        {
            query = query.Where(i => i.Type == issueType);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)}%";
            query = query.Where(i => EF.Functions.Like(i.Title, pattern));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.UpdatedAt).ThenByDescending(i => i.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var counts = await CommentCountsAsync(db, rows, ct);
        return TypedResults.Ok(new IssuePage(page, pageSize, total, [.. rows.Select(i => ToResponse(i, principal, counts.GetValueOrDefault(i.Id), null, null, null))]));
    }

    private static async Task<Results<Ok<IssueResponse>, NotFound>> GetAsync(
        int id, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct)
    {
        var issue = await Rows(db).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null || !IssueService.Visible(principal, issue))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await DetailAsync(db, issue, principal, issues, files, actions, ct));
    }

    /// <summary>What a Discover title page shows of issues, for every signed-in user.</summary>
    /// <param name="mediaType"><c>movie</c> or <c>series</c>.</param>
    /// <param name="tvdbId">A series' TVDB id, when Discover knows it.</param>
    private static async Task<Results<Ok<IssueTitleResponse>, ProblemHttpResult>> TitleAsync(
        string mediaType, int tmdbId, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, CancellationToken ct, int? tvdbId = null)
    {
        if (!Enum.TryParse<RequestMediaType>(mediaType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            return TypedResults.Problem(title: "Unknown media type", statusCode: StatusCodes.Status400BadRequest);
        }

        var title = await issues.TitleAsync(type, tmdbId, tvdbId, ct);
        var open = Rows(db).Where(i => i.Status == IssueStatus.Open);
        open = title.MovieId is { } movieId ? open.Where(i => i.MovieId == movieId)
            : title.SeriesId is { } seriesId ? open.Where(i => i.SeriesId == seriesId)
            : open.Where(i => i.MediaType == type && i.TmdbId == tmdbId);
        if (!IssueService.Approver(principal))
        {
            var me = principal.UserId();
            open = open.Where(i => i.ReportedById == me);
        }

        var rows = await open.OrderByDescending(i => i.UpdatedAt).Take(MaxPageSize).ToListAsync(ct);
        var counts = await CommentCountsAsync(db, rows, ct);
        return TypedResults.Ok(new IssueTitleResponse(
            title.InLibrary,
            title.InLibrary && principal.Has(UserPermission.ReportIssues),
            title.Versions,
            title.Seasons,
            [.. rows.Select(i => ToResponse(i, principal, counts.GetValueOrDefault(i.Id), null, null, null))]));
    }

    private static async Task<Results<Created<IssueResponse>, ProblemHttpResult>> CreateAsync(
        CreateIssueRequest body, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct)
    {
        var result = await issues.CreateAsync(principal, body, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var row = await Rows(db).FirstAsync(i => i.Id == result.Value!.Id, ct);
        return TypedResults.Created($"/api/v1/issues/{row.Id}", await DetailAsync(db, row, principal, issues, files, actions, ct));
    }

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> CommentAsync(
        int id, IssueMessageRequest body, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await issues.CommentAsync(principal, id, body?.Message, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> EditCommentAsync(
        int id, int commentId, IssueMessageRequest body, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await issues.EditCommentAsync(principal, id, commentId, body?.Message, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> DeleteCommentAsync(
        int id, int commentId, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await issues.DeleteCommentAsync(principal, id, commentId, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> ResolveAsync(
        int id, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await issues.ResolveAsync(principal, id, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> ReopenAsync(
        int id, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await issues.ReopenAsync(principal, id, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> SearchAsync(
        int id, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await actions.SearchAsync(principal, id, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> BlocklistAsync(
        int id, IssueBlocklistRequest? body, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await actions.BlocklistAsync(principal, id, body?.DeleteFile ?? false, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> DeleteFileAsync(
        int id, IssueDeleteFileRequest? body, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct) =>
        await Done((await actions.DeleteFileAsync(principal, id, body?.Search ?? true, ct)).Failure, id, principal, db, issues, files, actions, ct);

    private static async Task<Ok<IssueCountsResponse>> CountAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new IssueCountsResponse(await db.Issues.CountAsync(i => i.Status == IssueStatus.Open, ct)));

    /// <summary>A change went through: the issue as it is now; or the failure as a problem.</summary>
    private static async Task<Results<Ok<IssueResponse>, ProblemHttpResult>> Done(
        Failure? failure, int id, ClaimsPrincipal principal, BigarrDbContext db, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct)
    {
        if (failure is not null)
        {
            return failure.ToProblem();
        }

        db.ChangeTracker.Clear();
        var row = await Rows(db).FirstAsync(i => i.Id == id, ct);
        return TypedResults.Ok(await DetailAsync(db, row, principal, issues, files, actions, ct));
    }

    private static async Task<Dictionary<int, int>> CommentCountsAsync(BigarrDbContext db, List<Issue> rows, CancellationToken ct)
    {
        var ids = rows.Select(i => i.Id).ToList();
        return await db.IssueComments.AsNoTracking()
            .Where(c => ids.Contains(c.IssueId) && c.Kind == IssueCommentKind.Comment)
            .GroupBy(c => c.IssueId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
    }

    /// <summary>The issue with its thread; for an approver the actions; for the admin the file now when it changed.</summary>
    private static async Task<IssueResponse> DetailAsync(
        BigarrDbContext db, Issue issue, ClaimsPrincipal principal, IssueService issues, IssueFiles files, IssueActionService actions, CancellationToken ct)
    {
        var comments = await db.IssueComments.AsNoTracking().Include(c => c.Author)
            .Where(c => c.IssueId == issue.Id)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        var admin = principal.IsAdmin();
        var thread = comments.Select(c => new IssueCommentResponse(
            c.Id,
            c.Kind,
            c.Author is { } author ? new IssueUserRef(author.Id, author.Username) : null,
            c.Message,
            c.Action,
            c.Outcome,
            admin ? c.Detail : null,
            c.CreatedAt,
            c.EditedAt,
            issues.CanChange(principal, c),
            issues.CanChange(principal, c))).ToList();

        IssueActionsResponse? available = IssueService.Approver(principal) ? await actions.AvailableAsync(issue, ct) : null;
        IssueFile? current = admin ? await files.CurrentAsync(issue, ct) : null;
        return ToResponse(issue, principal, comments.Count(c => c.Kind == IssueCommentKind.Comment), thread, available, current);
    }

    private static IssueResponse ToResponse(
        Issue i, ClaimsPrincipal principal, int commentCount, List<IssueCommentResponse>? thread, IssueActionsResponse? actions, IssueFile? current)
    {
        var admin = principal.IsAdmin();
        var mine = i.ReportedById == principal.UserId();
        var may = mine || IssueService.Approver(principal);
        var reported = i.File;
        var changed = admin && thread is not null && (reported?.MediaFileId ?? reported?.EpisodeFileId) != (current?.Snapshot.MediaFileId ?? current?.Snapshot.EpisodeFileId);
        return new IssueResponse(
            i.Id,
            i.Type,
            i.Scope,
            i.Status,
            i.MediaType,
            i.TmdbId,
            i.Title,
            i.Year,
            i.PosterUrl,
            i.SeasonNumber,
            i.EpisodeLabel,
            i.VersionName,
            i.Message,
            new IssueUserRef(i.ReportedById, i.ReportedBy.Username),
            i.CreatedAt,
            i.UpdatedAt,
            i.ResolvedAt,
            i.ResolvedBy is { } resolver ? new IssueUserRef(resolver.Id, resolver.Username) : null,
            commentCount,
            mine,
            may && i.Status == IssueStatus.Open,
            may && i.Status == IssueStatus.Resolved,
            may,
            admin ? i.MovieId ?? i.SeriesId : null,
            admin ? File(reported) : null,
            changed ? File(current?.Snapshot) : null,
            changed,
            actions,
            thread);
    }

    private static IssueFileResponse? File(IssueFileSnapshot? file) =>
        file is null ? null : new IssueFileResponse(file.RelativePath, file.Size, file.Quality, file.ReleaseTitle, file.ReleaseGroup, file.IndexerName);
}
