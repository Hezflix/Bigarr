namespace Bigarr.Api.Features.Server.Backup;

/// <summary>Bound from <c>Bigarr:Backup</c>.</summary>
public sealed class BackupOptions
{
    public const string Section = "Bigarr:Backup";

    /// <summary>Days between two scheduled backups. Zero disables the scheduled job; backups by hand still work.</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>Scheduled backups older than this are deleted by the job. Backups made by hand follow the same rule.</summary>
    public int RetentionDays { get; set; } = 28;

    /// <summary>Folder of the zip files; relative paths are under the config directory.</summary>
    public string Directory { get; set; } = "backups";
}
