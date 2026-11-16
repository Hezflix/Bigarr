using Bigarr.Domain.Requests;

namespace Bigarr.Domain.Issues;

/// <summary>What is wrong, as the reporter puts it.</summary>
public enum IssueType
{
    Video = 1,
    Audio = 2,
    Subtitles = 3,
    Other = 4,
}

/// <summary>What the issue is about: a film, or a whole series, one of its seasons or one episode.</summary>
public enum IssueScope
{
    Movie = 1,
    Series = 2,
    Season = 3,
    Episode = 4,
}

/// <summary>An issue is open until the reporter or an approver resolves it; either may reopen it (STATE-MACHINES.md, "Issues").</summary>
public enum IssueStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>
/// A problem a user reports on a title of the library: bad video, audio, subtitles or anything else, on a film, a whole
/// series, a season or one episode, in one version. The version and, when there is one, the file at report time are kept
/// so an admin sees exactly what is complained about even after the file changed.
/// </summary>
public sealed class Issue
{
    public int Id { get; set; }

    public IssueType Type { get; set; }

    public IssueScope Scope { get; set; }

    public IssueStatus Status { get; set; } = IssueStatus.Open;

    public RequestMediaType MediaType { get; set; }

    public int? TmdbId { get; set; }

    public int? TvdbId { get; set; }

    public required string Title { get; set; }

    public int? Year { get; set; }

    public string? PosterUrl { get; set; }

    /// <summary>The library's film; null once the film left the library.</summary>
    public int? MovieId { get; set; }

    /// <summary>The library's series; null once the series left the library.</summary>
    public int? SeriesId { get; set; }

    /// <summary>The season of a season or episode issue.</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>The library's episode of an episode issue; null once it left the library.</summary>
    public int? EpisodeId { get; set; }

    /// <summary>The episode as the reporter saw it ("S01E02 · Title"), kept when the episode is gone.</summary>
    public string? EpisodeLabel { get; set; }

    /// <summary>The version concerned, by name (the library's default one unless the reporter picked another).</summary>
    public required string VersionName { get; set; }

    public int? MovieVersionId { get; set; }

    public int? SeriesVersionId { get; set; }

    /// <summary>The film's or the episode's file at report time, as the admin sees it. Null when there was none.</summary>
    public IssueFileSnapshot? File { get; set; }

    public required string Message { get; set; }

    public int ReportedById { get; set; }

    public Auth.User ReportedBy { get; set; } = null!;

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public int? ResolvedById { get; set; }

    public Auth.User? ResolvedBy { get; set; }

    public ICollection<IssueComment> Comments { get; set; } = [];
}

/// <summary>
/// The file an issue is about, frozen at report time: which file and which release produced it. The admin's only:
/// nothing here reaches the reporter.
/// </summary>
/// <param name="MediaFileId">The film's file row (films).</param>
/// <param name="EpisodeFileId">The episode's file row (series).</param>
/// <param name="RelativePath">The file inside its version's folder.</param>
/// <param name="ReleaseTitle">The release that produced the file, from its grab or its import; null when unknown.</param>
/// <param name="GrabId">The grab that produced the file, when Bigarr grabbed it: the release the blocklist action blocks.</param>
public sealed record IssueFileSnapshot(
    int? MediaFileId,
    int? EpisodeFileId,
    string RelativePath,
    long Size,
    string? Quality,
    string? ReleaseTitle,
    string? ReleaseGroup,
    string? IndexerName,
    int? GrabId);

public enum IssueCommentKind
{
    /// <summary>Written by the reporter or an approver.</summary>
    Comment = 1,

    /// <summary>An approver ran an action from the issue (search, blocklist, delete the file).</summary>
    Action = 2,

    Resolved = 3,

    Reopened = 4,
}

/// <summary>What an approver ran from an issue.</summary>
public enum IssueAction
{
    Search = 1,
    BlocklistAndSearch = 2,
    DeleteFile = 3,
}

/// <summary>
/// One entry of an issue's thread: a comment, or a line the app writes for an action, a resolve or a reopen. An action's
/// <see cref="Detail"/> may name releases and files: only the admin reads it.
/// </summary>
public sealed class IssueComment
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue Issue { get; set; } = null!;

    public IssueCommentKind Kind { get; set; } = IssueCommentKind.Comment;

    /// <summary>Who wrote it, or who ran the action; null once that user is gone.</summary>
    public int? AuthorId { get; set; }

    public Auth.User? Author { get; set; }

    /// <summary>The comment's text; empty for a line the app writes.</summary>
    public string Message { get; set; } = string.Empty;

    public IssueAction? Action { get; set; }

    /// <summary>How the action went, in a line every reader may see ("searched, a release was grabbed").</summary>
    public string? Outcome { get; set; }

    /// <summary>The action's details for the admin: release names, file paths.</summary>
    public string? Detail { get; set; }

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime? EditedAt { get; set; }
}
