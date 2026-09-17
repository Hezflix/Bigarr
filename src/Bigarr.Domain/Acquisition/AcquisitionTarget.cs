namespace Bigarr.Domain.Acquisition;

/// <summary>
/// Base table shared by everything the queue, the history, the blocklist and the decision engine
/// need to point at without knowing the business type (TPT inheritance, see blueprint §3.2).
/// </summary>
public abstract class AcquisitionTarget
{
    protected AcquisitionTarget(TargetKind kind)
    {
        Kind = kind;
    }

    public int Id { get; set; }

    public TargetKind Kind { get; private set; }

    public bool Monitored { get; set; } = true;

    /// <summary>Last automatic search, whatever triggered it. Drives the cutoff-unmet backoff with <see cref="SearchAttempts"/>.</summary>
    public DateTime? LastSearchAt { get; set; }

    /// <summary>Automatic searches in a row that found nothing to grab; reset by a grab or an import. The backoff doubles with it (blueprint §5).</summary>
    public int SearchAttempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GrabTarget> Grabs { get; set; } = [];
}
