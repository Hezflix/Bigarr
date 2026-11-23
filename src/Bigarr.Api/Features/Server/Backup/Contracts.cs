namespace Bigarr.Api.Features.Server.Backup;

public sealed record BackupResponse(string Name, long Size, DateTime CreatedAt, string Type);

/// <summary>A restore stages the file and stops the process: the database is swapped at the next start.</summary>
public sealed record RestoreResponse(string Name, bool RestartRequested, string Message);

/// <summary>The scheduled backup: every <see cref="IntervalDays"/> days (0: never), pruned after <see cref="RetentionDays"/> days.</summary>
public sealed record BackupSettingsResponse(int IntervalDays, int RetentionDays);

public sealed record BackupSettingsRequest(int IntervalDays, int RetentionDays);
