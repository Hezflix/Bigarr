using Bigarr.Domain.Issues;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Issues;

// What reaches whom: the reporter sees their own issues, the thread, and the version by name; approvers (admin, request
// manager) see everyone's and the actions; only the admin sees the file, the release that produced it, the indexer and
// the library ids. A field an audience must not see is null for it.

/// <param name="TmdbId">The title's TMDB id, as on its Discover page.</param>
/// <param name="TvdbId">A series' TVDB id when Discover knows it: the library may only know the series by it.</param>
/// <param name="SeasonNumber">Required for a season or an episode issue.</param>
/// <param name="EpisodeId">The library's episode, required for an episode issue (from the title's report options).</param>
/// <param name="VersionName">The version concerned; null takes the library's default one.</param>
public sealed record CreateIssueRequest(
    RequestMediaType MediaType,
    int TmdbId,
    IssueType Type,
    IssueScope Scope,
    string Message,
    int? TvdbId = null,
    int? SeasonNumber = null,
    int? EpisodeId = null,
    string? VersionName = null);

public sealed record IssueMessageRequest(string Message);

/// <param name="DeleteFile">Also delete the file (with its release blocklisted, the search then looks for another one).</param>
public sealed record IssueBlocklistRequest(bool DeleteFile = false);

/// <param name="Search">Search for the version, season or episode again once the file is gone.</param>
public sealed record IssueDeleteFileRequest(bool Search = true);

public sealed record IssueUserRef(int Id, string Username);

/// <summary>The file an issue is about. The admin's only.</summary>
/// <param name="Path">The file inside its version's folder.</param>
public sealed record IssueFileResponse(string Path, long Size, string? Quality, string? ReleaseTitle, string? ReleaseGroup, string? IndexerName);

/// <summary>What an approver may run from the issue now. <paramref name="ManagedBy"/> names why the file actions are off.</summary>
/// <param name="Search">Search the issue's film, season, episode or series again.</param>
/// <param name="Blocklist">The file's release is known and Bigarr owns the file.</param>
/// <param name="DeleteFile">The issue is about one file (film or episode), it is there, and Bigarr owns it.</param>
/// <param name="ManagedBy">"shadow" while shadow mode is on, or the followed Radarr/Sonarr that owns the version's files.</param>
public sealed record IssueActionsResponse(bool Search, bool Blocklist, bool DeleteFile, string? ManagedBy);

/// <param name="Outcome">How an action went, in words every reader may see.</param>
/// <param name="Detail">The action's release names and paths: the admin's only.</param>
public sealed record IssueCommentResponse(
    int Id,
    IssueCommentKind Kind,
    IssueUserRef? Author,
    string Message,
    IssueAction? Action,
    string? Outcome,
    string? Detail,
    DateTime CreatedAt,
    DateTime? EditedAt,
    bool CanEdit,
    bool CanDelete);

/// <param name="Mine">The caller reported it.</param>
/// <param name="ReportedBy">Always the reporter: themself for a user, anyone for an approver.</param>
/// <param name="LibraryId">The film's or the series' library id, for the admin's link.</param>
/// <param name="File">The file at report time: the admin's only.</param>
/// <param name="CurrentFile">The file there now when it is not the reported one (replaced, or gone: null with <paramref name="FileChanged"/>).</param>
/// <param name="Comments">The thread, oldest first; null in a list.</param>
public sealed record IssueResponse(
    int Id,
    IssueType Type,
    IssueScope Scope,
    IssueStatus Status,
    RequestMediaType MediaType,
    int? TmdbId,
    string Title,
    int? Year,
    string? PosterUrl,
    int? SeasonNumber,
    string? EpisodeLabel,
    string VersionName,
    string Message,
    IssueUserRef ReportedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    IssueUserRef? ResolvedBy,
    int CommentCount,
    bool Mine,
    bool CanResolve,
    bool CanReopen,
    bool CanComment,
    int? LibraryId,
    IssueFileResponse? File,
    IssueFileResponse? CurrentFile,
    bool FileChanged,
    IssueActionsResponse? Actions,
    List<IssueCommentResponse>? Comments);

public sealed record IssuePage(int Page, int PageSize, int TotalRecords, List<IssueResponse> Records);

public sealed record IssueCountsResponse(int Open);

/// <param name="FourK">The version Discover shows as 4K.</param>
public sealed record IssueVersionOption(string Name, bool IsDefault, bool FourK);

public sealed record IssueEpisodeOption(int Id, int EpisodeNumber, string Title, bool HasFile);

public sealed record IssueSeasonOption(int SeasonNumber, List<IssueEpisodeOption> Episodes);

/// <summary>
/// What a Discover title page needs for issues: whether the title is in the library and may be reported on, what can be
/// picked (version, season, episode), and the open issues the caller may see on it.
/// </summary>
/// <param name="CanReport">In the library, and the caller has "report issues".</param>
public sealed record IssueTitleResponse(
    bool InLibrary,
    bool CanReport,
    List<IssueVersionOption> Versions,
    List<IssueSeasonOption> Seasons,
    List<IssueResponse> Open);
