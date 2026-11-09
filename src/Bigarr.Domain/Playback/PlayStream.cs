using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Playback;

/// <summary>One session of a play on the media server: a player, a decision, a file, from one offset to another.</summary>
public sealed class PlayStream
{
    public int Id { get; set; }

    public int PlayId { get; set; }

    public Play Play { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public DateTime StoppedAt { get; set; }

    public long StartOffsetMs { get; set; }

    public long EndOffsetMs { get; set; }

    public int PausedSeconds { get; set; }

    /// <summary>Player, network, decision, source and stream formats, flattened into columns.</summary>
    public StreamDetails Details { get; set; } = new();

    /// <summary>The played file as Bigarr sees it: <see cref="StreamDetails.ServerFilePath"/> through the path mappings.</summary>
    public string? FilePath { get; set; }

    public int? VersionId { get; set; }

    public MovieVersion? Version { get; set; }

    /// <summary>Where an imported row came from (<c>tautulli:&lt;id&gt;</c>): a second import skips it. Null for live streams.</summary>
    public string? ExternalKey { get; set; }
}
