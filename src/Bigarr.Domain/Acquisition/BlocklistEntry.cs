namespace Bigarr.Domain.Acquisition;

/// <summary>Scoped to a managed version; one-shot versions read and feed the default version's list (§3.5).</summary>
public sealed class BlocklistEntry
{
    public int Id { get; set; }

    public int TargetId { get; set; }

    public AcquisitionTarget Target { get; set; } = null!;

    public string? ReleaseGuid { get; set; }

    public string? InfoHash { get; set; }

    public required string SourceTitle { get; set; }

    public int? IndexerId { get; set; }

    public string? IndexerName { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
