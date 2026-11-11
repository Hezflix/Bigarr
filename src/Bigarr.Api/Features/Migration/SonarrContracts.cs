namespace Bigarr.Api.Features.Migration;

public sealed record SonarrImportPreviewRequest(string Url, string ApiKey);

/// <summary>What one Sonarr holds, against what Bigarr already has, so the mapping can be chosen before anything is written.</summary>
public sealed record SonarrImportPreviewResponse(
    string SonarrVersion,
    int Series,
    /// <summary>Episodes Sonarr knows, aired or not, and how many have a file (its statistics).</summary>
    int Episodes,
    int EpisodeFiles,
    int Monitored,
    int AlreadyInLibrary,
    int Exclusions,
    /// <summary>Lines in Sonarr's history; the import copies those of the series it brings in.</summary>
    int HistoryRecords,
    /// <summary>Releases in Sonarr's blocklist; the import copies those of the series it brings in.</summary>
    int Blocklist,
    /// <summary>The version name the import proposes: <c>4k</c> when the profiles are mostly 2160p, <c>1080p</c> otherwise.</summary>
    string SuggestedVersionName,
    /// <summary>Series per type: <c>standard</c>, <c>anime</c>, <c>daily</c>.</summary>
    Dictionary<string, int> SeriesTypes,
    List<RadarrProfilePreview> Profiles,
    List<SonarrRootFolderPreview> RootFolders,
    List<RadarrTagPreview> Tags,
    /// <summary>Sonarr v3 language profiles, for information: Bigarr has none (languages live in custom formats).</summary>
    List<string> LanguageProfiles,
    /// <summary>What the import would do with each Sonarr custom format: new, reused, renamed ("name (Sonarr)").</summary>
    List<SonarrFormatPreview>? CustomFormats = null);

/// <param name="Reachable">The Sonarr path exists on this host as is: no path translation needed.</param>
/// <param name="SuggestedRootFolderId">A Bigarr series root folder with the same path.</param>
public sealed record SonarrRootFolderPreview(string Path, int Series, int? SuggestedRootFolderId, bool Reachable);

public sealed record SonarrImportRequest(
    string Url,
    string ApiKey,
    /// <summary>Name of the series version each imported show gets (or is matched on): <c>1080p</c>, or <c>4k</c> for a second Sonarr.</summary>
    string VersionName,
    /// <summary>
    /// Sonarr quality profile id → Bigarr quality profile id (Series or Both), or 0 to create a Series profile from the
    /// Sonarr one. A series whose profile is not mapped is skipped.
    /// </summary>
    Dictionary<int, int> ProfileMap,
    /// <summary>Sonarr root folder path → Bigarr series root folder id. A series whose root is not mapped is skipped.</summary>
    Dictionary<string, int> RootFolderMap,
    bool ImportTags = true,
    bool ImportExclusions = true,
    /// <summary>Copy Sonarr's history (grabs, imports, failures, deletions, renames) of every imported episode into Bigarr's.</summary>
    bool ImportHistory = true,
    /// <summary>Copy Sonarr's blocklist onto the episodes of the version, so rejected releases stay rejected.</summary>
    bool ImportBlocklist = true,
    /// <summary>Keep Sonarr's monitored flags (series, seasons, episodes); false imports everything unmonitored.</summary>
    bool KeepMonitored = true,
    /// <summary>Shadow mode: once the import is done, keep following this Sonarr with the same mapping.</summary>
    bool Follow = false,
    /// <summary>
    /// Give each mapped Series profile the qualities, cutoff and score thresholds of the Sonarr profile mapped to it, and its
    /// scores for the custom formats Bigarr has under the same name. A Both profile keeps its own.
    /// </summary>
    bool ImportProfileQualities = true,
    /// <summary>Sonarr's quality sizes become Bigarr's series sizes (the film sizes stay).</summary>
    bool ImportQualitySizes = true,
    /// <summary>
    /// Bring Sonarr's custom formats into the shared library (reused when alike, "name (Sonarr)" when the name is taken by
    /// another definition) and give the mapped Series profiles Sonarr's format scores and score thresholds.
    /// </summary>
    bool ImportCustomFormats = true,
    /// <summary>
    /// Copy Sonarr's naming (episode formats, series and season folders, multi-episode style, renaming on/off) into
    /// Settings → Media management. Import only: following Sonarr never rewrites it.
    /// </summary>
    bool ImportNaming = true);

/// <param name="TvdbId">Null when the line is not about a series (a profile).</param>
/// <param name="SeriesId">The series in Bigarr when it exists, so the report can link to it.</param>
public sealed record SonarrImportIssue(string Title, int? TvdbId, string Reason, int? SeriesId);

public sealed record SonarrImportStatusResponse(
    RadarrImportState State,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int Total,
    int Processed,
    int SeriesAdded,
    int VersionsAdded,
    int VersionsUpdated,
    int EpisodesAdded,
    int FilesAdded,
    int FilesMissing,
    int Skipped,
    int Failed,
    int TagsAdded,
    int ExclusionsAdded,
    int HistoryAdded,
    int BlocklistAdded,
    int ProfilesScored,
    int ProfileQualitiesCopied,
    string? Current,
    string? Error,
    List<SonarrImportIssue> Issues,
    /// <summary>Series profiles created from Sonarr's (a profile mapped to 0).</summary>
    int ProfilesCreated = 0,
    /// <summary>Series quality sizes taken from Sonarr's quality definitions.</summary>
    int QualitySizesUpdated = 0,
    int CustomFormatsAdded = 0,
    int CustomFormatsReused = 0,
    /// <summary>Imported as "name (Sonarr)": Bigarr has another format under that name.</summary>
    int CustomFormatsRenamed = 0,
    int CustomFormatsUpdated = 0,
    /// <summary>Sonarr's naming was copied into media management.</summary>
    bool NamingImported = false);
