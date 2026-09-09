using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.History;

/// <summary>
/// Append-only journal: the source of truth for "why is this file here". Foreign keys are nullable and
/// set to null on delete; the snapshots keep the row meaningful afterwards.
/// </summary>
public sealed class HistoryEvent
{
    public int Id { get; set; }

    public int? TargetId { get; set; }

    public AcquisitionTarget? Target { get; set; }

    public int? MovieId { get; set; }

    public Movie? Movie { get; set; }

    public int? TmdbId { get; set; }

    public string? MovieTitle { get; set; }

    public string? VersionName { get; set; }

    public HistoryEventType EventType { get; set; }

    public string? SourceTitle { get; set; }

    public int? IndexerId { get; set; }

    public string? IndexerName { get; set; }

    public int? MediaFileId { get; set; }

    public MediaFile? MediaFile { get; set; }

    /// <summary>Free-form details: size, seeders, scores, rejection reasons…</summary>
    public Dictionary<string, string> Data { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
