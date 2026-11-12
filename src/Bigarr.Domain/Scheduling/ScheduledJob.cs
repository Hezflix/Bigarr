namespace Bigarr.Domain.Scheduling;

/// <summary>
/// The persisted half of the scheduler: when a job last ran and how it went, so a restart resumes the
/// cadence instead of running everything at once. Intervals come from configuration, not from this row.
/// </summary>
public sealed class ScheduledJob
{
    public required string Name { get; set; }

    public DateTime? LastRunAt { get; set; }

    public int? LastDurationMs { get; set; }

    public bool? LastSucceeded { get; set; }

    /// <summary>One line the job wrote about what it did ("3 releases grabbed").</summary>
    public string? LastSummary { get; set; }

    public string? LastError { get; set; }

    public DateTime? NextRunAt { get; set; }
}
