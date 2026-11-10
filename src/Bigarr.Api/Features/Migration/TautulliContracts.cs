namespace Bigarr.Api.Features.Migration;

/// <param name="Path">The copy of <c>tautulli.db</c> on the Bigarr host: typed, or where an upload put it.</param>
/// <param name="MediaServerId">The Plex server the history comes from: its users, items and plays get the history.</param>
public sealed record TautulliImportPreviewRequest(string Path, int MediaServerId);

/// <summary>What a Tautulli database holds, against what Bigarr already has, before anything is written.</summary>
public sealed record TautulliImportPreviewResponse(
    string? TautulliVersion,
    int Users,
    /// <summary>Accounts Tautulli marks deleted (left the server); they come along with their history.</summary>
    int DeletedUsers,
    /// <summary>Accounts Bigarr already knows on this server, from the live sessions or an earlier import.</summary>
    int UsersKnown,
    DateTime? FirstPlayAt,
    DateTime? LastPlayAt,
    int HistoryRows,
    /// <summary>Plays once the rows are grouped (a resumed view is one play), films and episodes.</summary>
    int Plays,
    int MoviePlays,
    int EpisodePlays,
    /// <summary>Plays of anything else (music, photos, Live TV): left out.</summary>
    int OtherPlays,
    int Movies,
    int Episodes,
    /// <summary>Film plays whose film Bigarr has, by TMDB or IMDb id, or else by title and year.</summary>
    int MoviePlaysWithFilm,
    /// <summary>Of those, the plays whose version can be told (the library's folders, then the only version with a file or the played resolution).</summary>
    int MoviePlaysWithVersion,
    /// <summary>Film plays with no id yet but a Plex guid: the import asks Plex for their ids.</summary>
    int MoviePlaysAskPlex,
    /// <summary>Film plays left without a film: kept, under their title, with no link.</summary>
    int MoviePlaysUnmatched,
    /// <summary>Plays imported before: a re-run skips them.</summary>
    int AlreadyImported,
    /// <summary>Plays Bigarr followed live at the same time (same user, same item): the live play is kept, the row skipped.</summary>
    int OverlappingLive);

/// <param name="AskPlex">Ask the Plex server for the ids of items Tautulli only knows by a Plex guid, and for its libraries' folders.</param>
public sealed record TautulliImportRequest(string Path, int MediaServerId, bool AskPlex = true);

public sealed record TautulliUploadResponse(string Path, long Bytes);

/// <summary>A film the import could not link, with how many plays it has, for the report.</summary>
public sealed record TautulliImportIssue(string Title, int? Year, string Reason, int Plays);

public sealed record TautulliImportStatusResponse(
    RadarrImportState State,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    /// <summary>Grouped plays to go through.</summary>
    int Total,
    int Processed,
    int UsersAdded,
    int ItemsAdded,
    int PlaysAdded,
    int StreamsAdded,
    /// <summary>Plays imported before that gained rows Tautulli recorded since.</summary>
    int PlaysExtended,
    int AlreadyImported,
    int OverlappingLive,
    int OtherSkipped,
    int MoviePlays,
    int MoviePlaysWithFilm,
    int MoviePlaysWithVersion,
    int PlexAsked,
    int PlexResolved,
    string? Current,
    string? Error,
    List<TautulliImportIssue> Issues,
    /// <summary>The uploaded copy was deleted once its history was in.</summary>
    bool UploadDeleted);
