using System.Security.Claims;
using Bigarr.Api.Features.Discover;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Issues;

/// <summary>
/// The issue lifecycle (STATE-MACHINES.md, "Issues"): reported open on a title of the library, commented on by the reporter
/// and the approvers, resolved and reopened by either. Every step goes to the outbox for the notifications to come.
/// </summary>
public sealed class IssueService(BigarrDbContext db, IssueFiles files, LibraryAvailability library, OutboxWriter outbox, TimeProvider time)
{
    public const int MaxMessageLength = 4000;

    /// <summary>How long the author of a comment may still edit or delete it. The admin may at any time.</summary>
    public static readonly TimeSpan CommentEditWindow = TimeSpan.FromMinutes(15);

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Admins and request managers: they see every issue, act on it and resolve it.</summary>
    public static bool Approver(ClaimsPrincipal principal) =>
        principal.IsAdmin() || principal.IsInRole(nameof(UserRole.RequestManager));

    public static bool Visible(ClaimsPrincipal principal, Issue issue) =>
        Approver(principal) || issue.ReportedById == principal.UserId();

    /// <summary>The comment's author while the window is open, or the admin.</summary>
    public bool CanChange(ClaimsPrincipal principal, IssueComment comment) =>
        comment.Kind == IssueCommentKind.Comment
        && (principal.IsAdmin() || (comment.AuthorId == principal.UserId() && Now - comment.CreatedAt <= CommentEditWindow));

    public async Task<Result<Issue>> CreateAsync(ClaimsPrincipal principal, CreateIssueRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!principal.Has(UserPermission.ReportIssues))
        {
            return new Failure(StatusCodes.Status403Forbidden, "Not allowed", "You may not report issues.");
        }

        if (!Enum.IsDefined(body.MediaType) || !Enum.IsDefined(body.Type) || !Enum.IsDefined(body.Scope))
        {
            return Failure.Invalid("Unknown media type, issue type or scope");
        }

        if (Clean(body.Message) is not { } message)
        {
            return Failure.Invalid("A message is required", $"Say what is wrong, in at most {MaxMessageLength} characters.");
        }

        var movieScope = body.Scope == IssueScope.Movie;
        if (movieScope != (body.MediaType == RequestMediaType.Movie))
        {
            return Failure.Invalid("Scope does not match the title", "A film's issue is about the film; a series' about the series, a season or an episode.");
        }

        var issue = new Issue
        {
            Type = body.Type,
            Scope = body.Scope,
            MediaType = body.MediaType,
            TmdbId = body.TmdbId,
            Title = string.Empty,
            VersionName = string.Empty,
            Message = message,
            ReportedById = principal.UserId(),
            UpdatedAt = Now,
        };

        if (body.MediaType == RequestMediaType.Movie)
        {
            var movie = await db.Movies.AsNoTracking().Include(m => m.Versions).FirstOrDefaultAsync(m => m.TmdbId == body.TmdbId, ct);
            if (movie is null)
            {
                return Failure.NotFound("Film in the library");
            }

            if (Pick(movie.Versions.Select(v => (v.Id, v.Name, v.IsDefault)), body.VersionName) is not { } version)
            {
                return Failure.Invalid("Unknown version", $"{movie.Title} has no version named '{body.VersionName}'.");
            }

            (issue.Title, issue.Year, issue.PosterUrl, issue.MovieId) = (movie.Title, movie.Year, Poster(movie.Images), movie.Id);
            (issue.MovieVersionId, issue.VersionName) = (version.Id, version.Name);
            issue.File = (await files.MovieAsync(version.Id, ct))?.Snapshot;
        }
        else
        {
            var series = await SeriesAsync(body.TmdbId, body.TvdbId, ct);
            if (series is null)
            {
                return Failure.NotFound("Series in the library");
            }

            if (Pick(series.Versions.Select(v => (v.Id, v.Name, v.IsDefault)), body.VersionName) is not { } version)
            {
                return Failure.Invalid("Unknown version", $"{series.Title} has no version named '{body.VersionName}'.");
            }

            (issue.Title, issue.Year, issue.PosterUrl, issue.SeriesId, issue.TvdbId) = (series.Title, series.Year, Poster(series.Images), series.Id, series.TvdbId);
            (issue.SeriesVersionId, issue.VersionName) = (version.Id, version.Name);
            if (series.TmdbId is { } tmdb)
            {
                issue.TmdbId = tmdb;
            }

            if (body.Scope == IssueScope.Season)
            {
                if (body.SeasonNumber is not { } season || !await db.Episodes.AnyAsync(e => e.SeriesId == series.Id && e.SeasonNumber == season, ct))
                {
                    return Failure.Invalid("Unknown season", $"{series.Title} has no season {body.SeasonNumber} in the library.");
                }

                issue.SeasonNumber = season;
            }
            else if (body.Scope == IssueScope.Episode)
            {
                var episode = body.EpisodeId is { } episodeId ? await db.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == episodeId && e.SeriesId == series.Id, ct) : null;
                if (episode is null)
                {
                    return Failure.Invalid("Unknown episode", $"Pick one of the episodes of {series.Title}.");
                }

                (issue.SeasonNumber, issue.EpisodeId, issue.EpisodeLabel) = (episode.SeasonNumber, episode.Id, EpisodeLabel(episode.SeasonNumber, episode.EpisodeNumber, episode.Title));
                issue.File = (await files.EpisodeAsync(version.Id, episode.Id, ct))?.Snapshot;
            }
        }

        db.Issues.Add(issue);
        await db.SaveChangesAsync(ct);
        outbox.Publish(new IssueCreated(IssueRef.From(issue, principal.Identity?.Name ?? string.Empty)));
        await db.SaveChangesAsync(ct);
        return Result<Issue>.Ok(issue);
    }

    public async Task<Result<Issue>> ResolveAsync(ClaimsPrincipal principal, int id, CancellationToken ct) =>
        await MoveAsync(principal, id, IssueStatus.Open, IssueStatus.Resolved, ct);

    public async Task<Result<Issue>> ReopenAsync(ClaimsPrincipal principal, int id, CancellationToken ct) =>
        await MoveAsync(principal, id, IssueStatus.Resolved, IssueStatus.Open, ct);

    /// <summary>Open → resolved, resolved → open: the reporter or an approver, with a line in the thread and an event.</summary>
    private async Task<Result<Issue>> MoveAsync(ClaimsPrincipal principal, int id, IssueStatus from, IssueStatus to, CancellationToken ct)
    {
        var issue = await db.Issues.Include(i => i.ReportedBy).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null || !Visible(principal, issue))
        {
            return Failure.NotFound("Issue");
        }

        if (issue.Status != from)
        {
            return Failure.Conflict(to == IssueStatus.Resolved ? "Already resolved" : "Not resolved", $"The issue is {issue.Status.ToString().ToLowerInvariant()}.");
        }

        var now = Now;
        var who = principal.Identity?.Name ?? string.Empty;
        issue.Status = to;
        issue.UpdatedAt = now;
        (issue.ResolvedAt, issue.ResolvedById) = to == IssueStatus.Resolved ? (now, principal.UserId()) : ((DateTime?)null, (int?)null);
        db.IssueComments.Add(new IssueComment
        {
            IssueId = issue.Id,
            Kind = to == IssueStatus.Resolved ? IssueCommentKind.Resolved : IssueCommentKind.Reopened,
            AuthorId = principal.UserId(),
            CreatedAt = now,
        });
        var reference = IssueRef.From(issue, issue.ReportedBy.Username);
        outbox.Publish(to == IssueStatus.Resolved ? new IssueResolved(reference, who) : new IssueReopened(reference, who));
        await db.SaveChangesAsync(ct);
        return Result<Issue>.Ok(issue);
    }

    public async Task<Result<IssueComment>> CommentAsync(ClaimsPrincipal principal, int id, string? text, CancellationToken ct)
    {
        var issue = await db.Issues.Include(i => i.ReportedBy).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null || !Visible(principal, issue))
        {
            return Failure.NotFound("Issue");
        }

        if (Clean(text) is not { } message)
        {
            return Failure.Invalid("A message is required", $"At most {MaxMessageLength} characters.");
        }

        var comment = new IssueComment { IssueId = issue.Id, AuthorId = principal.UserId(), Message = message, CreatedAt = Now };
        db.IssueComments.Add(comment);
        issue.UpdatedAt = comment.CreatedAt;
        await db.SaveChangesAsync(ct);
        outbox.Publish(new IssueCommented(IssueRef.From(issue, issue.ReportedBy.Username), comment.Id, message, principal.Identity?.Name ?? string.Empty, principal.UserId()));
        await db.SaveChangesAsync(ct);
        return Result<IssueComment>.Ok(comment);
    }

    public async Task<Result<IssueComment>> EditCommentAsync(ClaimsPrincipal principal, int id, int commentId, string? text, CancellationToken ct)
    {
        var found = await ChangeableAsync(principal, id, commentId, ct);
        if (!found.IsSuccess)
        {
            return found;
        }

        if (Clean(text) is not { } message)
        {
            return Failure.Invalid("A message is required", $"At most {MaxMessageLength} characters.");
        }

        var comment = found.Value!;
        comment.Message = message;
        comment.EditedAt = Now;
        await db.SaveChangesAsync(ct);
        return Result<IssueComment>.Ok(comment);
    }

    public async Task<Result<bool>> DeleteCommentAsync(ClaimsPrincipal principal, int id, int commentId, CancellationToken ct)
    {
        var found = await ChangeableAsync(principal, id, commentId, ct);
        if (!found.IsSuccess)
        {
            return found.Failure!;
        }

        db.IssueComments.Remove(found.Value!);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task<Result<IssueComment>> ChangeableAsync(ClaimsPrincipal principal, int id, int commentId, CancellationToken ct)
    {
        var comment = await db.IssueComments.Include(c => c.Issue).FirstOrDefaultAsync(c => c.Id == commentId && c.IssueId == id, ct);
        if (comment is null || !Visible(principal, comment.Issue))
        {
            return Failure.NotFound("Comment");
        }

        if (!CanChange(principal, comment))
        {
            return new Failure(StatusCodes.Status403Forbidden, "Not allowed", $"Only its author changes a comment, within {CommentEditWindow.TotalMinutes:0} minutes; the admin at any time. The app's own lines stay.");
        }

        return Result<IssueComment>.Ok(comment);
    }

    /// <summary>What a Discover title page shows and offers for issues: see <see cref="IssueTitleResponse"/>.</summary>
    public async Task<(bool InLibrary, List<IssueVersionOption> Versions, List<IssueSeasonOption> Seasons, int? MovieId, int? SeriesId)> TitleAsync(
        RequestMediaType type, int tmdbId, int? tvdbId, CancellationToken ct)
    {
        var fourK = await library.FourKVersionAsync(ct);
        IssueVersionOption Option(string name, bool isDefault) => new(name, isDefault, fourK is not null && string.Equals(name, fourK, StringComparison.OrdinalIgnoreCase));
        if (type == RequestMediaType.Movie)
        {
            var movie = await db.Movies.AsNoTracking().Include(m => m.Versions).FirstOrDefaultAsync(m => m.TmdbId == tmdbId, ct);
            return movie is null
                ? (false, [], [], null, null)
                : (true, [.. movie.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Name).Select(v => Option(v.Name, v.IsDefault))], [], movie.Id, null);
        }

        var series = await SeriesAsync(tmdbId, tvdbId, ct);
        if (series is null)
        {
            return (false, [], [], null, null);
        }

        var episodes = await db.Episodes.AsNoTracking()
            .Where(e => e.SeriesId == series.Id)
            .OrderBy(e => e.SeasonNumber == 0).ThenBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => new { e.Id, e.SeasonNumber, e.EpisodeNumber, e.Title, HasFile = e.Targets.Any(t => t.EpisodeFileId != null) })
            .ToListAsync(ct);
        var seasons = episodes.GroupBy(e => e.SeasonNumber)
            .Select(g => new IssueSeasonOption(g.Key, [.. g.Select(e => new IssueEpisodeOption(e.Id, e.EpisodeNumber, e.Title, e.HasFile))]))
            .ToList();
        return (true, [.. series.Versions.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Name).Select(v => Option(v.Name, v.IsDefault))], seasons, null, series.Id);
    }

    private async Task<Domain.Tv.Series?> SeriesAsync(int tmdbId, int? tvdbId, CancellationToken ct) =>
        await db.Series.AsNoTracking().Include(s => s.Versions)
            .Where(s => s.TmdbId == tmdbId || (tvdbId != null && s.TvdbId == tvdbId))
            .OrderByDescending(s => s.TmdbId == tmdbId)
            .FirstOrDefaultAsync(ct);

    private static (int Id, string Name)? Pick(IEnumerable<(int Id, string Name, bool IsDefault)> versions, string? name)
    {
        var list = versions.ToList();
        var picked = string.IsNullOrWhiteSpace(name)
            ? list.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).FirstOrDefault()
            : list.FirstOrDefault(v => string.Equals(v.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return picked.Name is null ? null : (picked.Id, picked.Name);
    }

    private static string? Poster(IEnumerable<MediaImage> images) => images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl;

    public static string EpisodeLabel(int season, int episode, string? title) =>
        string.IsNullOrWhiteSpace(title) ? $"S{season:00}E{episode:00}" : $"S{season:00}E{episode:00} · {title}";

    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxMessageLength ? null : trimmed;
    }
}
