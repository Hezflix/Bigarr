namespace Bigarr.Api.Features.Server.Backup;

public sealed record BackupResponse(string Name, long Size, DateTime CreatedAt, string Type);

/// <summary>A restore stages the file and stops the process: the database is swapped at the next start.</summary>
public sealed record RestoreResponse(string Name, bool RestartRequested, string Message);
