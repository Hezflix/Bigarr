namespace Bigarr.Domain.Acquisition;

/// <summary>
/// Join between a grab and its targets. <see cref="Active"/> mirrors the grab state so SQLite can enforce
/// "at most one active grab per target" with a partial unique index.
/// </summary>
public sealed class GrabTarget
{
    public int GrabId { get; set; }

    public Grab Grab { get; set; } = null!;

    public int TargetId { get; set; }

    public AcquisitionTarget Target { get; set; } = null!;

    public bool Active { get; set; }
}
