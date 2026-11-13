namespace Bigarr.Api.Features.Server.Tasks;

/// <summary>
/// A recurring job: the scheduler runs it on its interval and on demand, records the outcome, and
/// shows it in the UI. Implementations are scoped services: a fresh scope per run.
/// </summary>
public interface IScheduledJob
{
    /// <summary>Stable camelCase id, the key of the <c>ScheduledJobs</c> row and of the API route.</summary>
    string Name { get; }

    string DisplayName { get; }

    TimeSpan Interval { get; }

    /// <summary>A disabled job never runs on its own; it can still be triggered by hand.</summary>
    bool Enabled { get; }

    /// <summary>Grace period after startup before the first run: a restart must not hammer the indexers.</summary>
    TimeSpan InitialDelay { get; }

    /// <summary>
    /// Runs on its own even with <c>Bigarr:Scheduler:Enabled</c> false: a job that only reads (Plex's watch history)
    /// is as safe in shadow mode as the playback monitor that follows the same server.
    /// </summary>
    bool RunsWhenSchedulerDisabled => false;

    /// <summary>
    /// Takes minutes (a pass over the whole library): run in its own lane, beside the others, so the short jobs (RSS,
    /// the follows, health) keep their cadence while it runs.
    /// </summary>
    bool LongRunning => false;

    /// <summary>
    /// Runs only when something plans it (sets its next run), once, and does not come back on its own: the RSS quick
    /// recheck. <see cref="Interval"/> is then the delay its planner uses.
    /// </summary>
    bool OneShot => false;

    /// <summary>Does the work and returns one line about it ("15 releases, 2 grabbed"), or null.</summary>
    Task<string?> RunAsync(CancellationToken ct);
}

/// <summary>Bound from <c>Bigarr:Scheduler</c>. Disabled in the tests, where jobs run through the API.</summary>
public sealed class SchedulerOptions
{
    public const string Section = "Bigarr:Scheduler";

    public bool Enabled { get; set; } = true;
}
