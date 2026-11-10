using System.Diagnostics;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Server.Tasks;

/// <summary>
/// Runs jobs, scheduled or by hand, and writes the outcome to memory and to the <c>ScheduledJobs</c> table. The
/// scheduler loop and the tasks API both go through here, so a job never runs twice at once.
/// </summary>
public sealed class TaskRunner(IServiceScopeFactory scopeFactory, SchedulerState state, SchedulerSignal signal, IOptions<SchedulerOptions> options, TimeProvider time, ILogger<TaskRunner> logger)
{
    /// <summary>The jobs that keep their schedule when the scheduler is disabled (<see cref="IScheduledJob.RunsWhenSchedulerDisabled"/>).</summary>
    private readonly HashSet<string> _alwaysOn = new(StringComparer.Ordinal);

    /// <summary>Whether a job runs on its own: every job with the scheduler on, only the always-on ones with it off.</summary>
    private bool Scheduled(IScheduledJob job) => job.Enabled && (options.Value.Enabled || job.RunsWhenSchedulerDisabled);

    /// <summary>Loads what the previous run left and decides when each job is first due after this start.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var rows = await db.ScheduledJobs.AsNoTracking().ToDictionaryAsync(j => j.Name, StringComparer.Ordinal, ct);
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            var runtime = state.For(job.Name);
            if (rows.TryGetValue(job.Name, out var row))
            {
                runtime.Load(row);
            }

            // Resume the cadence: not before the grace period, not later than one interval after the last run.
            var earliest = now + job.InitialDelay;
            var cadence = runtime.LastRunAt is { } last ? last + job.Interval : earliest;
            runtime.NextRunAt = job.Enabled ? (cadence > earliest ? cadence : earliest) : null;
            if (job.RunsWhenSchedulerDisabled)
            {
                _alwaysOn.Add(job.Name);
            }
        }
    }

    public async Task<List<ScheduledTaskResponse>> ListAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return scope.ServiceProvider.GetServices<IScheduledJob>()
            .Select(job =>
            {
                var runtime = state.For(job.Name);
                return new ScheduledTaskResponse(
                    job.Name,
                    job.DisplayName,
                    (int)job.Interval.TotalMinutes,
                    Scheduled(job),
                    runtime.Running,
                    runtime.LastRunAt,
                    runtime.LastDurationMs,
                    runtime.LastSucceeded,
                    runtime.LastSummary,
                    runtime.LastError,
                    Scheduled(job) ? runtime.NextRunAt : null);
            })
            .OrderBy(t => t.DisplayName)
            .ToList();
    }

    /// <summary>Jobs whose time has come, oldest due first; with the scheduler disabled, only the always-on ones.</summary>
    public List<string> Due(DateTime now) =>
        ScheduledNames().Where(n => state.TryGet(n, out var r) && !r.Running && r.NextRunAt is { } next && next <= now)
            .OrderBy(n => state.NextRunAt(n))
            .ToList();

    public DateTime? NextDue() =>
        ScheduledNames().Select(n => state.NextRunAt(n)).Where(n => n is not null).Min();

    /// <summary>Whether anything runs on its own: false with the scheduler disabled and no always-on job.</summary>
    public bool AnyScheduled => options.Value.Enabled || _alwaysOn.Count > 0;

    private IEnumerable<string> ScheduledNames() => options.Value.Enabled ? state.Names : state.Names.Where(_alwaysOn.Contains);

    public async Task<Result<TaskRunResponse>> RunAsync(string name, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IScheduledJob>().FirstOrDefault(j => string.Equals(j.Name, name, StringComparison.Ordinal));
        if (job is null)
        {
            return Failure.NotFound($"Task '{name}'");
        }

        var runtime = state.For(job.Name);
        if (!await runtime.Lock.WaitAsync(0, ct))
        {
            return Failure.Conflict("Task already running", $"{job.DisplayName} is running; wait for it to finish.");
        }

        try
        {
            var startedAt = time.GetUtcNow().UtcDateTime;
            var stopwatch = Stopwatch.StartNew();
            string? summary = null;
            string? error = null;
            try
            {
                summary = await job.RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger.LogError(ex, "Task {Task} failed", job.Name);
            }

            stopwatch.Stop();
            var finishedAt = time.GetUtcNow().UtcDateTime;
            runtime.LastRunAt = startedAt;
            runtime.LastDurationMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
            runtime.LastSucceeded = error is null;
            runtime.LastSummary = summary;
            runtime.LastError = error;
            runtime.NextRunAt = job.Enabled ? finishedAt + job.Interval : null;

            await PersistAsync(scope.ServiceProvider, job.Name, runtime, ct);
            signal.Notify();

            if (error is null)
            {
                logger.LogInformation("Task {Task} done in {Elapsed} ms{Summary}", job.Name, runtime.LastDurationMs, summary is null ? string.Empty : $": {summary}");
            }

            return Result<TaskRunResponse>.Ok(new TaskRunResponse(job.Name, error is null, runtime.LastDurationMs.Value, summary, error, startedAt));
        }
        finally
        {
            runtime.Lock.Release();
        }
    }

    private static async Task PersistAsync(IServiceProvider services, string name, SchedulerState.JobRuntime runtime, CancellationToken ct)
    {
        var db = services.GetRequiredService<BigarrDbContext>();
        db.ChangeTracker.Clear(); // the job may have left tracked entities behind
        var row = await db.ScheduledJobs.FirstOrDefaultAsync(j => j.Name == name, ct);
        if (row is null)
        {
            row = new ScheduledJob { Name = name };
            db.ScheduledJobs.Add(row);
        }

        row.LastRunAt = runtime.LastRunAt;
        row.LastDurationMs = runtime.LastDurationMs;
        row.LastSucceeded = runtime.LastSucceeded;
        row.LastSummary = runtime.LastSummary is { Length: > 1024 } s ? s[..1024] : runtime.LastSummary;
        row.LastError = runtime.LastError is { Length: > 2048 } e ? e[..2048] : runtime.LastError;
        row.NextRunAt = runtime.NextRunAt;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>The clock of the scheduler: sleeps until the next job is due, or until a run or a trigger wakes it up.</summary>
public sealed class SchedulerRunner(TaskRunner runner, SchedulerSignal signal, IOptions<SchedulerOptions> options, TimeProvider time, ILogger<SchedulerRunner> logger) : BackgroundService
{
    public static readonly TimeSpan MaxSleep = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await runner.InitializeAsync(stoppingToken);
            if (!options.Value.Enabled)
            {
                logger.LogInformation("Scheduler disabled (Bigarr:Scheduler:Enabled = false): tasks only run when triggered, but for the read-only ones");
                if (!runner.AnyScheduled)
                {
                    return;
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = time.GetUtcNow().UtcDateTime;
                foreach (var name in runner.Due(now))
                {
                    try
                    {
                        await runner.RunAsync(name, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Scheduled run of {Task} crashed", name);
                    }
                }

                var next = runner.NextDue();
                var sleep = next is { } due ? due - time.GetUtcNow().UtcDateTime : MaxSleep;
                sleep = sleep < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : sleep > MaxSleep ? MaxSleep : sleep;
                await signal.WaitAsync(sleep, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
