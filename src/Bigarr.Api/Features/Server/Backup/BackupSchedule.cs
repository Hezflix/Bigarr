using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Server.Backup;

/// <summary>
/// The interval and the retention in force: what was saved in System → Backups, else <see cref="BackupOptions"/>.
/// Held in memory because <see cref="IScheduledJob.Interval"/> is read synchronously; loaded at startup, replaced on save.
/// </summary>
public sealed class BackupSchedule(IOptions<BackupOptions> options)
{
    public const int MaxIntervalDays = 365;

    public const int MaxRetentionDays = 3650;

    private Saved _saved = new(null, null);

    public int IntervalDays => _saved.IntervalDays ?? options.Value.IntervalDays;

    public int RetentionDays => Math.Max(1, _saved.RetentionDays ?? options.Value.RetentionDays);

    public void Apply(int? intervalDays, int? retentionDays) => Volatile.Write(ref _saved, new Saved(intervalDays, retentionDays));

    private sealed record Saved(int? IntervalDays, int? RetentionDays);
}

/// <summary>Reads the saved schedule before the scheduler starts (a restored database brings its own back).</summary>
public sealed class BackupScheduleLoader(BigarrDbContext db, BackupSchedule schedule) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = await UserSettings.ReadAsync(db, cancellationToken);
        schedule.Apply(settings.BackupIntervalDays, settings.BackupRetentionDays);
    }
}

/// <summary>Reads and saves the schedule; a save moves the next scheduled backup at once.</summary>
public sealed class BackupSettingsService(BigarrDbContext db, BackupSchedule schedule, TaskRunner tasks)
{
    public BackupSettingsResponse Get() => new(schedule.IntervalDays, schedule.RetentionDays);

    public async Task<Result<BackupSettingsResponse>> UpdateAsync(BackupSettingsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IntervalDays is < 0 or > BackupSchedule.MaxIntervalDays)
        {
            return Failure.Invalid("Invalid interval", $"A number of days between 0 (no scheduled backup) and {BackupSchedule.MaxIntervalDays}.");
        }

        if (request.RetentionDays is < 1 or > BackupSchedule.MaxRetentionDays)
        {
            return Failure.Invalid("Invalid retention", $"A number of days between 1 and {BackupSchedule.MaxRetentionDays}.");
        }

        var settings = await UserSettings.EditAsync(db, ct);
        settings.BackupIntervalDays = request.IntervalDays;
        settings.BackupRetentionDays = request.RetentionDays;
        await db.SaveChangesAsync(ct);

        schedule.Apply(request.IntervalDays, request.RetentionDays);
        await tasks.RescheduleAsync(BackupJob.JobName, ct);
        return Result<BackupSettingsResponse>.Ok(Get());
    }
}
