namespace Bigarr.Domain.Acquisition;

/// <summary>
/// Durable mapping between one or more targets and a download tracked in the client.
/// Survives restarts; the in-memory queue is a projection joined on <see cref="DownloadId"/>.
/// </summary>
public sealed class Grab
{
    public int Id { get; set; }

    /// <summary>FK added when the Indexers slice lands. Name is snapshotted for display and history.</summary>
    public int? IndexerId { get; set; }

    public string? IndexerName { get; set; }

    /// <summary>FK added when the Downloads slice lands.</summary>
    public int? DownloadClientId { get; set; }

    public string? DownloadClientName { get; set; }

    /// <summary>Identifier returned by the download client (info hash for qBittorrent).</summary>
    public string? DownloadId { get; set; }

    public required string ReleaseTitle { get; set; }

    public string? ReleaseGuid { get; set; }

    public string? InfoHash { get; set; }

    public long Size { get; set; }

    public IndexerFlags IndexerFlags { get; set; }

    public DownloadProtocol Protocol { get; set; } = DownloadProtocol.Torrent;

    [SetWhenAdded]
    public DateTime GrabbedAt { get; set; }

    /// <summary>Series grabs: which numbering the release matched its episodes on. Null for films.</summary>
    public Tv.EpisodeNumbering? Numbering { get; set; }

    /// <summary>Last poll where the client still listed this download. Drives the <see cref="GrabState.Removed"/> transition.</summary>
    public DateTime? LastSeenAt { get; set; }

    public GrabState State { get; private set; } = GrabState.Grabbed;

    public List<StatusMessage> StatusMessages { get; set; } = [];

    public ICollection<GrabTarget> Targets { get; set; } = [];

    public bool IsActive => State.IsActive();

    public GrabTarget AddTarget(AcquisitionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var link = new GrabTarget { Grab = this, Target = target, TargetId = target.Id, Active = IsActive };
        Targets.Add(link);
        return link;
    }

    /// <summary>Moves to <paramref name="next"/> and mirrors the active flag on every target link.</summary>
    public void TransitionTo(GrabState next)
    {
        if (State.IsTerminal())
        {
            throw new InvalidOperationException($"Grab {Id} is in terminal state {State} and cannot move to {next}.");
        }

        State = next;
        var active = next.IsActive();
        foreach (var target in Targets)
        {
            target.Active = active;
        }
    }
}
