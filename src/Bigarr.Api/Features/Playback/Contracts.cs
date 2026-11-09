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
    PlaybackUserRef User,
    PlaybackItemRef Item,
    PlaybackState State,
    long ViewOffsetMs,
    DateTime StartedAt,
    int PausedSeconds,
    int? VersionId,
    string? VersionName,
    StreamDetails Details);

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
    StreamDetails Details);

public sealed record PlaysPageResponse(int Page, int PageSize, int TotalRecords, List<PlayResponse> Records);

public sealed record PlaybackUserResponse(int Id, string Name, string? ThumbUrl, bool IsOwner, int Plays, DateTime LastSeenAt);

/// <summary>Plays of one film for one version; a null version gathers the plays whose version could not be told.</summary>
public sealed record VersionPlaysResponse(int? VersionId, string? VersionName, int Plays, int Users, int Transcodes, DateTime? LastPlayedAt);

public sealed record PlaybackSettingsDto(int MinimumPlay, MinimumPlayUnit MinimumPlayUnit, int WatchedPercent);
