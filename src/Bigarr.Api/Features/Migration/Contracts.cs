namespace Bigarr.Api.Features.Migration;

public sealed record RadarrImportPreviewRequest(string Url, string ApiKey);

/// <summary>What one Radarr holds, against what Bigarr already has, so the mapping can be chosen before anything is written.</summary>
public sealed record RadarrImportPreviewResponse(
    string RadarrVersion,
    int Movies,
    int WithFile,
    int Monitored,
    int AlreadyInLibrary,
    int Exclusions,
    /// <summary>Lines in Radarr's history; the import copies those of the films it brings in.</summary>
    int HistoryRecords,
    /// <summary>Releases in Radarr's blocklist; the import copies those of the films it brings in.</summary>
    int Blocklist,
    int CustomFormats,
    /// <summary>The version name the import proposes: <c>4k</c> when the files are mostly 2160p, <c>1080p</c> otherwise.</summary>
    string SuggestedVersionName,
    List<RadarrProfilePreview> Profiles,
    List<RadarrRootFolderPreview> RootFolders,
    List<RadarrTagPreview> Tags);

public sealed record RadarrProfilePreview(int Id, string Name, int Movies, int? SuggestedProfileId);

/// <param name="Reachable">The Radarr path exists on this host as is: no path translation needed.</param>
public sealed record RadarrRootFolderPreview(string Path, int Movies, int? SuggestedRootFolderId, bool Reachable);

public sealed record RadarrTagPreview(int Id, string Label, int Movies);

public sealed record RadarrImportRequest(
    string Url,
    string ApiKey,
    /// <summary>Name of the version each imported film gets (or is matched on): <c>1080p</c> for a standard Radarr, <c>4k</c> for a 4K one.</summary>
    string VersionName,
    /// <summary>Radarr quality profile id → Bigarr quality profile id. A film whose profile is not mapped is skipped.</summary>
    Dictionary<int, int> ProfileMap,
    /// <summary>Radarr root folder path → Bigarr root folder id. A film whose root is not mapped is skipped.</summary>
    Dictionary<string, int> RootFolderMap,
    bool ImportTags = true,
    bool ImportExclusions = true,
    /// <summary>Copy Radarr's history (grabs, imports, failures, deletions, renames) of every imported film into Bigarr's.</summary>
    bool ImportHistory = true,
    /// <summary>Copy Radarr's blocklist of every imported film onto the version it becomes, so rejected releases stay rejected.</summary>
    bool ImportBlocklist = true,
    /// <summary>
    /// Copy Radarr's custom formats (Radarr's definition wins over a Bigarr format of the same name) and give each mapped
    /// Bigarr profile the format scores and score thresholds of the Radarr profile mapped to it.
    /// </summary>
    bool ImportCustomFormats = true,
    /// <summary>Keep Radarr's monitored flag; false imports everything unmonitored.</summary>
    bool KeepMonitored = true,
    /// <summary>Films without a file in Radarr become fileless versions; false leaves them out.</summary>
    bool IncludeMoviesWithoutFile = true,
    /// <summary>
    /// Shadow mode: once the import is done, keep following this Radarr with the same mapping (its grabs for the
    /// comparison, the films and files it adds) instead of leaving it as a one-time import.
    /// </summary>
    bool Follow = false);

/// <summary>What one pass of the follow reads from a Radarr, handed to the import's per-film rules.</summary>
public sealed record RadarrFollowData(
    List<Infrastructure.RadarrImport.RadarrProfile> Profiles,
    List<Infrastructure.RadarrImport.RadarrTag> Tags,
    List<Infrastructure.RadarrImport.RadarrMovie> Movies,
    List<Infrastructure.RadarrImport.RadarrHistoryItem> History,
    List<Infrastructure.RadarrImport.RadarrBlocklistItem> Blocklist);

public enum RadarrImportState
{
    Idle = 0,
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
}

/// <param name="TmdbId">Null when the line is not about a film (a custom format, a profile).</param>
/// <param name="MovieId">The film in Bigarr when it exists, so the report can link to it.</param>
public sealed record RadarrImportIssue(string Title, int? TmdbId, string Reason, int? MovieId);

public sealed record RadarrImportStatusResponse(
    RadarrImportState State,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int Total,
    int Processed,
    int MoviesAdded,
    int VersionsAdded,
    int VersionsUpdated,
    int FilesAdded,
    int FilesMissing,
    int Skipped,
    int Failed,
    int TagsAdded,
    int ExclusionsAdded,
    int HistoryAdded,
    int BlocklistAdded,
    int CustomFormatsAdded,
    int CustomFormatsUpdated,
    int ProfilesScored,
    string? Current,
    string? Error,
    List<RadarrImportIssue> Issues);
