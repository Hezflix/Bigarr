using Bigarr.Domain.Auth;
using Bigarr.Domain.Discover;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Migration;

/// <param name="Path">The copy of Seerr's <c>db.sqlite3</c> on the Bigarr host: typed, or where an upload put it.</param>
public sealed record SeerrImportPreviewRequest(string Path);

/// <param name="AskTmdb">Ask TMDB for the title, year and poster of requested or reported titles the library does not have.</param>
/// <param name="ImportSliders">Bring Seerr's Discover rows: their order, which are on, and the custom rows Bigarr can show.</param>
public sealed record SeerrImportRequest(string Path, bool AskTmdb = true, bool ImportSliders = true);

public sealed record SeerrUploadResponse(string Path, long Bytes);

/// <summary>How a Seerr account finds its Bigarr user.</summary>
public enum SeerrUserMatch
{
    /// <summary>Brought by an earlier run.</summary>
    AlreadyImported = 1,

    /// <summary>A Bigarr user signs in with the same Plex account.</summary>
    PlexId = 2,

    /// <summary>A Bigarr user of the same name (or whose Plex account has that name).</summary>
    Username = 3,

    /// <summary>Seerr's owner (its first account, an admin) is Bigarr's admin.</summary>
    Owner = 4,

    /// <summary>Created, signing in with Plex later.</summary>
    NewPlex = 5,

    /// <summary>Created, but with no Plex account (a local, Jellyfin or Emby user of Seerr): no way to sign in yet.</summary>
    NewWithoutSignIn = 6,
}

/// <summary>One Seerr account and what the import does with it.</summary>
/// <param name="BigarrUsername">The matched user, or the name the new one gets.</param>
/// <param name="Role">The role a new user gets; a matched user keeps theirs.</param>
/// <param name="Skipped">Seerr permissions Bigarr has nothing for.</param>
public sealed record SeerrUserLine(
    int SeerrId,
    string Name,
    SeerrUserMatch Match,
    string BigarrUsername,
    UserRole Role,
    UserPermissions Permissions,
    bool DiscordId,
    int Requests,
    int Issues,
    List<string> Skipped);

/// <summary>A requested or reported title the library does not have: its requests and issues are imported without a link.</summary>
public sealed record SeerrTitleLine(RequestMediaType MediaType, int TmdbId, int? TvdbId, int Requests, int Issues);

/// <summary>A Seerr row left out, and why.</summary>
public sealed record SeerrSkippedLine(string Kind, int SeerrId, string What, string Reason);

/// <summary>Something Seerr has that Bigarr does not bring, for every row of its kind.</summary>
public sealed record SeerrNotImported(string What, int Count, string Why);

/// <summary>A Seerr Discover row and what Bigarr makes of it.</summary>
/// <param name="Kind">Bigarr's row kind (<see cref="DiscoverSliderKind"/>, camel case); null when skipped. A string, so the shared enum stays non-null.</param>
public sealed record SeerrSliderLine(string Name, string? Title, bool Enabled, string? Kind, string? Value, string? Skipped);

public sealed record SeerrRequestCounts(
    int Total,
    int Movies,
    int Series,
    int FourK,
    int Pending,
    int Approved,
    int Declined,
    int Failed,
    int Completed,
    /// <summary>Requests whose title the library has: linked to it.</summary>
    int InLibrary,
    int NotInLibrary,
    int AlreadyImported,
    int Skipped,
    /// <summary>Pending, approved or failed requests for a title and version a Bigarr request already holds: imported beside it.</summary>
    int OverlappingBigarr);

public sealed record SeerrIssueCounts(int Total, int Open, int Resolved, int Comments, int InLibrary, int NotInLibrary, int AlreadyImported, int CommentsAlreadyImported, int Skipped);

/// <summary>What a Seerr database holds against what Bigarr has, before anything is written.</summary>
public sealed record SeerrImportPreviewResponse(
    /// <summary>"Seerr", "Jellyseerr" or "Overseerr", told by the columns of the file.</summary>
    string Flavor,
    List<SeerrUserLine> Users,
    SeerrRequestCounts Requests,
    SeerrIssueCounts Issues,
    /// <summary>Titles the library lacks, most requested first.</summary>
    List<SeerrTitleLine> TitlesNotInLibrary,
    /// <summary>Titles to ask TMDB about for their name and poster (the import's TMDB option).</summary>
    int TitlesToAskTmdb,
    List<SeerrSliderLine> Sliders,
    List<SeerrSkippedLine> Skipped,
    List<SeerrNotImported> NotImported);

/// <summary>A row the import could not bring, or brought without its title or user, for the report.</summary>
public sealed record SeerrImportIssue(string Kind, int SeerrId, string What, string Reason);

public sealed record SeerrImportStatusResponse(
    RadarrImportState State,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    /// <summary>Users, requests and issues to go through.</summary>
    int Total,
    int Processed,
    int UsersAdded,
    int UsersMatched,
    /// <summary>Matched users who got their Discord id from Seerr.</summary>
    int UsersUpdated,
    int RequestsAdded,
    int IssuesAdded,
    int CommentsAdded,
    int SlidersAdded,
    int SlidersUpdated,
    int AlreadyImported,
    int Skipped,
    /// <summary>Titles the library lacks whose name TMDB gave.</summary>
    int TitlesFromTmdb,
    string? Current,
    string? Error,
    List<SeerrImportIssue> Issues,
    bool UploadDeleted);
