using Bigarr.Domain.MediaServers;

namespace Bigarr.Domain.Playback;

public enum PlaybackState
{
    Playing = 1,
    Paused = 2,
    Buffering = 3,
}

/// <summary>
/// A stream playing now, kept in the database so a restart of Bigarr loses nothing: when the session ends it becomes a
/// <see cref="PlayStream"/> (or nothing, under the minimum play time) and the row goes away.
/// </summary>
public sealed class PlaybackSession
{
    public int Id { get; set; }

    public int MediaServerId { get; set; }

    public MediaServer MediaServer { get; set; } = null!;

    /// <summary>Plex's session key: reused after a Plex restart, so never an identity on its own.</summary>
    public required string SessionKey { get; set; }

    /// <summary>Plex's <c>Session.id</c>, stable for the life of the stream.</summary>
    public string? ServerSessionId { get; set; }

    public required string RatingKey { get; set; }

    public int UserId { get; set; }

    public MediaServerUser User { get; set; } = null!;

    public int ItemId { get; set; }

    public MediaServerItem Item { get; set; } = null!;

    public PlaybackState State { get; set; } = PlaybackState.Playing;

    public long StartOffsetMs { get; set; }

    public long ViewOffsetMs { get; set; }

    public long MaxOffsetMs { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Last word from the server about this stream: where it stops if it vanishes.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Set while paused: the pause is added to <see cref="PausedSeconds"/> when it ends.</summary>
    public DateTime? PausedSince { get; set; }

    public int PausedSeconds { get; set; }

    /// <summary>The latest picture of the stream, copied to the <see cref="PlayStream"/> at the end.</summary>
    public StreamDetails Details { get; set; } = new();

    /// <summary>The played file through the path mappings, and the version it belongs to.</summary>
    public string? FilePath { get; set; }

    public int? MovieId { get; set; }

    public int? VersionId { get; set; }

    public string? VersionName { get; set; }
}
