using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;

namespace Bigarr.Domain.Events;

/// <summary>
/// The issue at the moment of the event: what an issue notification (R5) needs, without asking Bigarr back. No file,
/// release or indexer: a notification may reach the reporter.
/// </summary>
/// <param name="SeasonNumber">The season of a season or episode issue.</param>
/// <param name="EpisodeLabel">"S01E02 · Title" for an episode issue.</param>
/// <param name="ReportedBy">The username of who reported it.</param>
/// <param name="ReportedByUserId">Their id, to find their Discord id and opt-ins.</param>
public sealed record IssueRef(
    int Id,
    IssueType IssueType,
    IssueScope Scope,
    IssueStatus Status,
    RequestMediaType MediaType,
    int? TmdbId,
    int? TvdbId,
    string Title,
    int? Year,
    string? PosterUrl,
    int? SeasonNumber,
    string? EpisodeLabel,
    string VersionName,
    string Message,
    string ReportedBy,
    int ReportedByUserId)
{
    public static IssueRef From(Issue issue, string reportedBy)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new IssueRef(issue.Id, issue.Type, issue.Scope, issue.Status, issue.MediaType, issue.TmdbId, issue.TvdbId, issue.Title, issue.Year, issue.PosterUrl,
            issue.SeasonNumber, issue.EpisodeLabel, issue.VersionName, issue.Message, reportedBy, issue.ReportedById);
    }
}

public sealed record IssueCreated(IssueRef Issue) : DomainEvent;

/// <param name="CommentedBy">The username of who wrote the comment.</param>
/// <param name="CommentedByUserId">Their id: a notification skips the author.</param>
public sealed record IssueCommented(IssueRef Issue, int CommentId, string Message, string CommentedBy, int CommentedByUserId) : DomainEvent;

/// <param name="ResolvedBy">The reporter or an approver.</param>
public sealed record IssueResolved(IssueRef Issue, string ResolvedBy) : DomainEvent;

public sealed record IssueReopened(IssueRef Issue, string ReopenedBy) : DomainEvent;
