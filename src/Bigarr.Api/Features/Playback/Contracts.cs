using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Domain.Playback;

namespace Bigarr.Api.Features.Playback;

public sealed record PlaybackServerStatus(int Id, string Name, bool Connected, string? Error, DateTime? Since);

public sealed record PlaybackUserRef(int Id, string Name, string? ThumbUrl);

public sealed record PlaybackItemRef(
    int Id,
    MediaItemKind Kind,
    string Title,
    int? Year,
    string? ShowTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    long? DurationMs,
    string? LibraryTitle,
    int? MovieId);

public sealed record PlaybackSessionResponse(
    int Id,
    int ServerId,
    string SessionKey,
    PlaybackUserRef User,
    PlaybackItemRef Item,
    PlaybackState State,
    long ViewOffsetMs,
    DateTime StartedAt,
    int PausedSeconds,
    int? VersionId,
    string? VersionName,
    StreamDetails Details,
    PlexTranscode? Transcode = null,
    string? PosterUrl = null,
    string? FanartUrl = null,
    PlaybackLocation? Location = null,
    bool CanStop = false);

public sealed record NowPlayingResponse(List<PlaybackServerStatus> Servers, List<PlaybackSessionResponse> Sessions);

public sealed record PlayResponse(
    int Id,
    PlaybackUserRef User,
    PlaybackItemRef Item,
    DateTime StartedAt,
    DateTime StoppedAt,
    int PlayedSeconds,
    int PausedSeconds,
    long MaxOffsetMs,
    long? DurationMs,
    bool Watched,
    PlaySource Source,
    int? MovieId,
    int? VersionId,
    string? VersionName,
    int Streams,
    StreamDecision? Decision,
    string? Player,
    string? Platform,
    bool? Local);

public enum PlaySort
{
    StartedAt = 1,
    User = 2,
    Title = 3,
    Version = 4,
    Played = 5,
}

public sealed record PlayStreamResponse(
    int Id,
    DateTime StartedAt,
    DateTime StoppedAt,
    long StartOffsetMs,
    long EndOffsetMs,
    int PausedSeconds,
    string? FilePath,
    int? VersionId,
    string? VersionName,
    StreamDetails Details,
    PlaybackLocation? Location = null);

public sealed record PlaysPageResponse(int Page, int PageSize, int TotalRecords, List<PlayResponse> Records);

/// <summary>The optional message the viewer sees on their player when the stream is stopped.</summary>
public sealed record StopStreamRequest(string? Message);

/// <summary>An address a user played from, with its place, how many streams came from it and when.</summary>
public sealed record PlaybackAddressResponse(string IpAddress, PlaybackLocation? Location, int Streams, DateTime FirstSeenAt, DateTime LastSeenAt, string? LastPlayer, string? LastPlatform);

/// <summary>
/// What the Plays page filters on; every part is optional. <c>From</c>/<c>To</c> bound the start of the play (UTC, the end
/// excluded); <c>Version</c> is a version name (<c>4k</c>), across films; <c>Decision</c> is the play's furthest decision,
/// a transcode in any of its streams making it a transcode; <c>Search</c> looks in the title and the show's title;
/// <c>Unmatched</c> keeps the film plays linked to no Bigarr film.
/// </summary>
public sealed record PlayFilter(
    int? UserId = null,
    int? MovieId = null,
    int? VersionId = null,
    string? Version = null,
    MediaItemKind? Kind = null,
    DateTime? From = null,
    DateTime? To = null,
    StreamDecision? Decision = null,
    string? Search = null,
    bool Unmatched = false);

/// <summary><c>Name</c> is the one Bigarr shows (the display name when set), <c>ServerName</c> the account's on the server.</summary>
public sealed record PlaybackUserResponse(int Id, string Name, string? ThumbUrl, bool IsOwner, int Plays, DateTime LastSeenAt, string ServerName, bool KeepHistory);

/// <summary>
/// The user page's numbers: plays and watch time over the last day, week, month and since the start, players, decisions.
/// Where the user played from is <c>/users/{id}/addresses</c> (<see cref="PlaybackAddressResponse"/>), with the places.
/// </summary>
public sealed record PlaybackUserStatsResponse(
    int Id,
    string Name,
    string? DisplayName,
    string? ThumbUrl,
    bool IsOwner,
    bool KeepHistory,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    DateTime? LastPlayedAt,
    List<WatchTimeResponse> WatchTime,
    List<PlayerStatsResponse> Players,
    DecisionCountsResponse Decisions);

/// <summary>Plays started in the last <c>Days</c> days, or ever when null.</summary>
public sealed record WatchTimeResponse(int? Days, int Plays, long Seconds);

public sealed record PlayerStatsResponse(string Player, string? Platform, int Plays, long Seconds, DateTime LastPlayedAt);

/// <summary>Plays by their furthest decision; <c>Unknown</c> counts the plays without any stream detail (Plex's history).</summary>
public sealed record DecisionCountsResponse(int DirectPlay, int DirectStream, int Transcode, int Unknown);

/// <summary>A null <c>DisplayName</c> or blank one goes back to the server's name; a null <c>KeepHistory</c> leaves it.</summary>
public sealed record UpdatePlaybackUserRequest(string? DisplayName, bool? KeepHistory);

/// <summary>Plays of one film for one version; a null version gathers the plays whose version could not be told.</summary>
public sealed record VersionPlaysResponse(int? VersionId, string? VersionName, int Plays, int Users, int Transcodes, DateTime? LastPlayedAt);

public sealed record PlaybackSettingsDto(int MinimumPlay, MinimumPlayUnit MinimumPlayUnit, int WatchedPercent);
