using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Features.Server.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Server.Backup;

/// <summary>
/// Backups (blueprint M7): a zip holding a consistent snapshot of the SQLite database (<c>VACUUM INTO</c>, so a backup
/// taken under load is still a valid file) plus a manifest. Restoring stages the database next to the live one and asks
/// for a restart; <see cref="DatabaseInitializer"/> swaps the files before touching anything, then migrates.
/// </summary>
public sealed partial class BackupService(BigarrDbContext db, IOptions<BigarrOptions> bigarr, IOptions<BackupOptions> options, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<BackupService> logger)
{
    public const string DatabaseEntry = "bigarr.db";

    public const string ManifestEntry = "manifest.json";

    /// <summary>What a restore leaves for the next start. Kept next to the database so a plain file move does the swap.</summary>
    public const string StagedRestoreSuffix = ".restore";

    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    public string Directory => Path.IsPathRooted(options.Value.Directory) ? options.Value.Directory : Path.Combine(bigarr.Value.ConfigDirectory, options.Value.Directory);

    public List<BackupResponse> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        return new DirectoryInfo(Directory).EnumerateFiles("*.zip")
            .Where(f => NameRegex().IsMatch(f.Name))
            .Select(f => new BackupResponse(f.Name, f.Length, f.LastWriteTimeUtc, f.Name.Contains("_scheduled_", StringComparison.Ordinal) ? "scheduled" : "manual"))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task<BackupResponse> CreateAsync(bool scheduled, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var now = time.GetUtcNow().UtcDateTime;
        var name = $"bigarr_backup_{(scheduled ? "scheduled_" : string.Empty)}v{SystemEndpoints.Version.Replace('+', '-')}_{now:yyyy.MM.dd_HH.mm.ss}.zip";
        var target = Path.Combine(Directory, name);
        var snapshot = Path.Combine(Directory, $".{Guid.NewGuid():N}.db");

        try
        {
            // VACUUM INTO writes a compact, transactionally consistent copy without locking writers out.
            // ExecuteSqlAsync binds {snapshot} as a parameter (SQLite accepts a bound filename here), so no manual escaping is needed.
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {snapshot}", ct);

            var partial = target + ".partial";
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, DatabaseEntry, CompressionLevel.Optimal);
                var manifest = zip.CreateEntry(ManifestEntry);
                await using var stream = manifest.Open();
                await JsonSerializer.SerializeAsync(stream, new { version = SystemEndpoints.Version, createdAt = now, database = DatabaseEntry, scheduled }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, ct);
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            TryDelete(snapshot);
            TryDelete(target + ".partial");
        }

        var info = new FileInfo(target);
        logger.LogInformation("Backup written: {Name} ({Size} bytes)", name, info.Length);
        return new BackupResponse(name, info.Length, info.LastWriteTimeUtc, scheduled ? "scheduled" : "manual");
    }

    public Result<bool> Delete(string name)
    {
        var path = Resolve(name);
        if (path is null || !File.Exists(path))
        {
            return Failure.NotFound("Backup");
        }

        File.Delete(path);
        return Result<bool>.Ok(true);
    }

    /// <summary>The zip's path when <paramref name="name"/> is one of ours (no path tricks), else null.</summary>
    public string? Resolve(string name) =>
        NameRegex().IsMatch(name) && name == Path.GetFileName(name) ? Path.Combine(Directory, name) : null;

    /// <summary>Copies the database out of the zip next to the live one, checks it is SQLite, and schedules the restart that swaps it in.</summary>
    public async Task<Result<RestoreResponse>> RestoreAsync(string name, CancellationToken ct)
    {
        var path = Resolve(name);
        if (path is null || !File.Exists(path))
        {
            return Failure.NotFound("Backup");
        }

        var staged = bigarr.Value.DatabasePath + StagedRestoreSuffix;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(DatabaseEntry);
            if (entry is null)
            {
                return Failure.Invalid("Not a Bigarr backup", $"The archive has no {DatabaseEntry} entry.");
            }

            await using (var source = entry.Open())
            await using (var destination = File.Create(staged))
            {
                await source.CopyToAsync(destination, ct);
            }

            var header = new byte[SqliteHeader.Length];
            await using (var check = File.OpenRead(staged))
            {
                var read = await check.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
                if (read < header.Length || !header.AsSpan().SequenceEqual(SqliteHeader))
                {
                    TryDelete(staged);
                    return Failure.Invalid("Not a Bigarr backup", "The database entry is not an SQLite file.");
                }
            }
        }
        catch (InvalidDataException ex)
        {
            TryDelete(staged);
            return Failure.Invalid("Not a Bigarr backup", ex.Message);
        }

        logger.LogWarning("Restore of {Name} staged at {Path}: stopping so the next start swaps the database in", name, staged);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), CancellationToken.None);
            lifetime.StopApplication();
        }, CancellationToken.None);

        return Result<RestoreResponse>.Ok(new RestoreResponse(name, true, "The backup is staged. Bigarr is restarting; the restored database is in place when it comes back."));
    }

    /// <summary>Deletes what is older than the retention. Returns how many went.</summary>
    public int Prune(DateTime now)
    {
        var retention = TimeSpan.FromDays(Math.Max(1, options.Value.RetentionDays));
        var gone = 0;
        foreach (var backup in List().Where(b => now - b.CreatedAt > retention))
        {
            if (Delete(backup.Name).IsSuccess)
            {
                gone++;
            }
        }

        return gone;
    }

    /// <summary>Called by the initializer before opening the database: a staged file replaces the live one, the old one is kept aside once.</summary>
    public static bool ApplyStagedRestore(string databasePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var staged = databasePath + StagedRestoreSuffix;
        if (!File.Exists(staged))
        {
            return false;
        }

        if (File.Exists(databasePath))
        {
            var aside = databasePath + ".before-restore";
            File.Move(databasePath, aside, overwrite: true);
            logger.LogInformation("Previous database kept at {Path}", aside);
        }

        // WAL and shared-memory files belong to the database they were written with.
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        File.Move(staged, databasePath);
        logger.LogWarning("Restored the database from a staged backup");
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [GeneratedRegex(@"^bigarr_backup_[A-Za-z0-9._-]+\.zip$")]
    private static partial Regex NameRegex();
}

/// <summary>The scheduled backup (blueprint §3.7): one zip every <see cref="BackupOptions.IntervalDays"/>, older ones pruned.</summary>
public sealed class BackupJob(BackupService backups, IOptions<BackupOptions> options, TimeProvider time) : IScheduledJob
{
    public const string JobName = "backup";

    public string Name => JobName;

    public string DisplayName => "Backup";

    public TimeSpan Interval => TimeSpan.FromDays(Math.Max(1, options.Value.IntervalDays));

    public bool Enabled => options.Value.IntervalDays > 0;

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(10);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var created = await backups.CreateAsync(scheduled: true, ct);
        var pruned = backups.Prune(time.GetUtcNow().UtcDateTime);
        return $"{created.Name} written ({created.Size / 1024} KiB), {pruned} old backup(s) pruned";
    }
}
