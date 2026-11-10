namespace Bigarr.Api.Features.Shadow;

/// <summary>Shared by create and update. On update a blank or masked <see cref="ApiKey"/> keeps the stored key.</summary>
public sealed record FollowedSonarrRequest(
    string Name,
    string Url,
    string? ApiKey,
    /// <summary>The series version this Sonarr's shows are in Bigarr: <c>1080p</c>, <c>4k</c>.</summary>
    string VersionName,
    Dictionary<int, int>? ProfileMap = null,
    Dictionary<string, int>? RootFolderMap = null,
    bool ImportTags = true,
    bool KeepMonitored = true,
    bool Enabled = true);

/// <summary>A followed Sonarr; its key never leaves Bigarr (<see cref="FollowedRadarrService.KeyMask"/>).</summary>
public sealed record FollowedSonarrResponse(
    int Id,
    string Name,
    string Url,
    string ApiKey,
    string VersionName,
    Dictionary<int, int> ProfileMap,
    Dictionary<string, int> RootFolderMap,
    bool ImportTags,
    bool KeepMonitored,
    bool Enabled,
    /// <summary>The next pass reads Sonarr's history from here (a few minutes earlier, to be safe).</summary>
    DateTime HistoryCheckpoint,
    DateTime? LastSyncAt,
    string? LastSummary,
    string? LastError,
    DateTime? LastErrorAt,
    DateTime? LastWebhookAt,
    string? LastWebhookEvent,
    /// <summary>Grabs read from this Sonarr so far, and those still downloading on its side.</summary>
    int Grabs,
    int InFlight,
    DateTime CreatedAt);

public sealed record FollowedSonarrTestResponse(string? AppName, string? Version, int Series);
