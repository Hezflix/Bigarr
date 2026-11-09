using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Playback;

public enum PlaySource
{
    /// <summary>Followed live from the media server's sessions.</summary>
    Live = 1,

    /// <summary>Only known from the server's own watch history: no stream detail.</summary>
    ServerHistory = 2,

    /// <summary>Imported from a Tautulli database.</summary>
    Tautulli = 3,
}

/// <summary>
/// One view of an item by a user, whatever the number of sessions it took: stopping at 40 minutes and resuming the
/// next day is one play of two streams (Tautulli's grouped history). The stats count these rows.
/// </summary>
public sealed class Play
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public MediaServerUser User { get; set; } = null!;

    public int ItemId { get; set; }

    public MediaServerItem Item { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime StoppedAt { get; set; }

    /// <summary>Real watching time: pauses excluded.</summary>
    public int PlayedSeconds { get; set; }

    public int PausedSeconds { get; set; }

    /// <summary>The furthest position reached, the base of <see cref="Watched"/>.</summary>
    public long MaxOffsetMs { get; set; }

    /// <summary>The item's duration when it was played.</summary>
    public long? DurationMs { get; set; }

    public bool Watched { get; set; }

    public PlaySource Source { get; set; } = PlaySource.Live;

    /// <summary>The film played, kept when the item later loses its link. Null for an episode or an unmanaged film.</summary>
    public int? MovieId { get; set; }

    public Movie? Movie { get; set; }

    /// <summary>The version whose file was played, when it could be told.</summary>
    public int? VersionId { get; set; }

    public MovieVersion? Version { get; set; }

    /// <summary>Snapshot of the version's name: the stats keep it after the version is deleted.</summary>
    public string? VersionName { get; set; }

    public List<PlayStream> Streams { get; set; } = [];
}
