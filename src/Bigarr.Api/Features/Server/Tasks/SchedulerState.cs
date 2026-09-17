using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Scheduling;

namespace Bigarr.Api.Features.Server.Tasks;

/// <summary>Wakes the scheduler loop early (a run just finished, a job was triggered).</summary>
public sealed class SchedulerSignal : Signal;

/// <summary>The in-memory half of the scheduler: what is running, when each job is due. Loaded from the table at startup.</summary>
public sealed class SchedulerState
{
    private readonly ConcurrentDictionary<string, JobRuntime> _jobs = new(StringComparer.Ordinal);

    public JobRuntime For(string name) => _jobs.GetOrAdd(name, _ => new JobRuntime());

    public bool TryGet(string name, out JobRuntime runtime) => _jobs.TryGetValue(name, out runtime!);

    public DateTime? NextRunAt(string name) => _jobs.TryGetValue(name, out var runtime) ? runtime.NextRunAt : null;

    public bool IsRunning(string name) => _jobs.TryGetValue(name, out var runtime) && runtime.Running;

    public IEnumerable<string> Names => _jobs.Keys;

    public sealed class JobRuntime
    {
        /// <summary>One run at a time per job, scheduled or manual.</summary>
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public bool Running => Lock.CurrentCount == 0;

        public DateTime? NextRunAt { get; set; }

        public DateTime? LastRunAt { get; set; }

        public int? LastDurationMs { get; set; }

        public bool? LastSucceeded { get; set; }

        public string? LastSummary { get; set; }

        public string? LastError { get; set; }

        public void Load(ScheduledJob row)
        {
            ArgumentNullException.ThrowIfNull(row);
            LastRunAt = row.LastRunAt;
            LastDurationMs = row.LastDurationMs;
            LastSucceeded = row.LastSucceeded;
            LastSummary = row.LastSummary;
            LastError = row.LastError;
            NextRunAt = row.NextRunAt;
        }
    }
}
