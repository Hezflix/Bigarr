namespace Bigarr.Api.Features.Shadow;

/// <summary>Shared by create and update. On update a blank or masked <see cref="ApiKey"/> keeps the stored key.</summary>
public sealed record FollowedRadarrRequest(
    string Name,
    string Url,
    string? ApiKey,
    /// <summary>The version this Radarr's films are in Bigarr: <c>1080p</c>, <c>4k</c>.</summary>
    string VersionName,
    Dictionary<int, int>? ProfileMap = null,
    Dictionary<string, int>? RootFolderMap = null,
    bool ImportTags = true,
    bool KeepMonitored = true,
    bool IncludeMoviesWithoutFile = true,
    bool Enabled = true);

/// <summary>A followed Radarr; its key never leaves Bigarr (<see cref="FollowedRadarrService.KeyMask"/>).</summary>
public sealed record FollowedRadarrResponse(
    int Id,
    string Name,
    string Url,
    string ApiKey,
    string VersionName,
    Dictionary<int, int> ProfileMap,
    Dictionary<string, int> RootFolderMap,
    bool ImportTags,
    bool KeepMonitored,
    bool IncludeMoviesWithoutFile,
    bool Enabled,
    /// <summary>The next pass reads Radarr's history from here (a few minutes earlier, to be safe).</summary>
    DateTime HistoryCheckpoint,
    DateTime? LastSyncAt,
    string? LastSummary,
    string? LastError,
    DateTime? LastErrorAt,
    /// <summary>The last call of Radarr's webhook and its event, when Radarr is set up to call it.</summary>
    DateTime? LastWebhookAt,
    string? LastWebhookEvent,
    /// <summary>Grabs read from this Radarr so far, and those still downloading on its side.</summary>
    int Grabs,
    int InFlight,
    DateTime CreatedAt);

public sealed record FollowedRadarrTestResponse(string? AppName, string? Version, int Movies);

/// <summary>Shadow mode at a glance: is it on, how much each side has recorded, who is followed.</summary>
public sealed record ShadowStatusResponse(
    bool Enabled,
    int Picks,
    DateTime? LastPickAt,
    int RadarrGrabs,
    int Sightings,
    List<FollowedRadarrResponse> Radarrs,
    int SeriesPicks = 0,
    int SonarrGrabs = 0,
    List<FollowedSonarrResponse>? Sonarrs = null);
