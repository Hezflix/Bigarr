using System.Text.Json;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// Radarr v3 shapes, reduced to what Seerr and Bazarr read plus what makes a record look like Radarr's.
/// Null members are left out of the JSON (see <see cref="RadarrCompat.Json"/>).
/// </summary>
public sealed record RadarrLanguage(int Id, string Name);

public sealed record RadarrAlternateTitle(int Id, string SourceType, int MovieMetadataId, string Title);

/// <param name="Url">Relative, served by <c>/MediaCover</c>; Bazarr derives <c>poster-500.jpg</c> from it.</param>
public sealed record RadarrImage(string CoverType, string Url, string RemoteUrl);

public sealed record RadarrRating(int Votes, double Value, string Type = "user");

public sealed record RadarrRatings(RadarrRating? Tmdb);

public sealed record RadarrQualityDefinition(int Id, string Name, string Source, int Resolution, string Modifier);

public sealed record RadarrRevision(int Version, int Real, bool IsRepack);

public sealed record RadarrQuality(RadarrQualityDefinition Quality, RadarrRevision Revision);

public sealed record RadarrCustomFormat(int Id, string Name);

public sealed record RadarrMediaInfo(
    long? AudioBitrate,
    double? AudioChannels,
    string? AudioCodec,
    string? AudioLanguages,
    int AudioStreamCount,
    int? VideoBitDepth,
    long? VideoBitrate,
    string? VideoCodec,
    double VideoFps,
    string? VideoDynamicRange,
    string? VideoDynamicRangeType,
    /// <summary><c>1920x1080</c>: what a 4K Seerr and Bazarr read.</summary>
    string? Resolution,
    string? RunTime,
    string? ScanType,
    string? Subtitles);

public sealed record RadarrMovieFile(
    int Id,
    /// <summary>The compat movie id, i.e. the version id.</summary>
    int MovieId,
    string RelativePath,
    string Path,
    long Size,
    DateTime DateAdded,
    string? SceneName,
    string? ReleaseGroup,
    string? Edition,
    List<RadarrLanguage> Languages,
    RadarrQuality Quality,
    List<RadarrCustomFormat> CustomFormats,
    int CustomFormatScore,
    int IndexerFlags,
    RadarrMediaInfo? MediaInfo,
    string? OriginalFilePath,
    bool QualityCutoffNotMet);

public sealed record RadarrStatistics(int MovieFileCount, long SizeOnDisk, List<string> ReleaseGroups);

public sealed record RadarrCollection(string Title, int TmdbId);

public sealed record RadarrMovie
{
    /// <summary>The <see cref="Bigarr.Domain.Movies.MovieVersion"/> id, never the film's. Zero on a lookup result that is not in the view.</summary>
    public int Id { get; init; }

    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    public RadarrLanguage? OriginalLanguage { get; init; }

    public List<RadarrAlternateTitle> AlternateTitles { get; init; } = [];

    public int SecondaryYearSourceId { get; init; }

    public required string SortTitle { get; init; }

    public long SizeOnDisk { get; init; }

    public required string Status { get; init; }

    public string? Overview { get; init; }

    public DateTime? InCinemas { get; init; }

    public DateTime? PhysicalRelease { get; init; }

    public DateTime? DigitalRelease { get; init; }

    public DateTime? ReleaseDate { get; init; }

    public List<RadarrImage> Images { get; init; } = [];

    public string Website { get; init; } = string.Empty;

    public string? RemotePoster { get; init; }

    public int Year { get; init; }

    public string YouTubeTrailerId { get; init; } = string.Empty;

    public string Studio { get; init; } = string.Empty;

    /// <summary>The version's folder: root folder + folder name. Bazarr joins it with <c>movieFile.relativePath</c>.</summary>
    public string? Path { get; init; }

    public int QualityProfileId { get; init; }

    /// <summary>Legacy alias of <see cref="QualityProfileId"/> some clients still send.</summary>
    public int ProfileId { get; init; }

    public bool HasFile { get; init; }

    public int MovieFileId { get; init; }

    public bool Monitored { get; init; }

    public required string MinimumAvailability { get; init; }

    public bool IsAvailable { get; init; }

    public string? FolderName { get; init; }

    public int Runtime { get; init; }

    public required string CleanTitle { get; init; }

    public string? ImdbId { get; init; }

    public int TmdbId { get; init; }

    /// <summary>The TMDB id as a string, common to every version of the film.</summary>
    public required string TitleSlug { get; init; }

    public string? RootFolderPath { get; init; }

    public string? Folder { get; init; }

    public string? Certification { get; init; }

    public List<string> Genres { get; init; } = [];

    public List<int> Tags { get; init; } = [];

    public DateTime Added { get; init; }

    public RadarrRatings? Ratings { get; init; }

    public RadarrMovieFile? MovieFile { get; init; }

    public RadarrCollection? Collection { get; init; }

    public double Popularity { get; init; }

    public DateTime? LastSearchTime { get; init; }

    public RadarrStatistics? Statistics { get; init; }
}

/// <summary>What Seerr posts and puts. Unknown members are ignored, like Radarr does.</summary>
public sealed record RadarrMovieRequest
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public int QualityProfileId { get; init; }

    public int ProfileId { get; init; }

    public string? TitleSlug { get; init; }

    public string? MinimumAvailability { get; init; }

    public int TmdbId { get; init; }

    public int Year { get; init; }

    public string? RootFolderPath { get; init; }

    public bool Monitored { get; init; } = true;

    public List<int>? Tags { get; init; }

    public RadarrAddOptions? AddOptions { get; init; }
}

public sealed record RadarrAddOptions(bool SearchForMovie = false, string? Monitor = null);

public sealed record RadarrQualityProfileItem(RadarrQualityDefinition? Quality, List<RadarrQualityProfileItem> Items, bool Allowed, int? Id = null, string? Name = null);

public sealed record RadarrFormatItem(int Format, string Name, int Score);

public sealed record RadarrQualityProfile(
    int Id,
    string Name,
    bool UpgradeAllowed,
    int Cutoff,
    List<RadarrQualityProfileItem> Items,
    int MinFormatScore,
    int CutoffFormatScore,
    int MinUpgradeFormatScore,
    List<RadarrFormatItem> FormatItems,
    RadarrLanguage Language);

public sealed record RadarrRootFolder(int Id, string Path, bool Accessible, long? FreeSpace, List<object> UnmappedFolders);

public sealed record RadarrTag(int Id, string Label);

public sealed record RadarrTagRequest(int Id, string? Label);

public sealed record RadarrStatusMessage(string Title, List<string> Messages);

public sealed record RadarrQueueItem(
    int Id,
    /// <summary>Version id.</summary>
    int MovieId,
    List<RadarrLanguage> Languages,
    RadarrQuality Quality,
    List<RadarrCustomFormat> CustomFormats,
    int CustomFormatScore,
    long Size,
    string Title,
    long Sizeleft,
    string? Timeleft,
    DateTime? EstimatedCompletionTime,
    DateTime Added,
    string Status,
    string TrackedDownloadStatus,
    string TrackedDownloadState,
    List<RadarrStatusMessage> StatusMessages,
    string? ErrorMessage,
    string DownloadId,
    string Protocol,
    string DownloadClient,
    bool DownloadClientHasPostImportCategory,
    string? Indexer,
    string? OutputPath);

public sealed record RadarrHistoryRecord(
    int Id,
    int MovieId,
    string? SourceTitle,
    List<RadarrLanguage> Languages,
    RadarrQuality Quality,
    List<RadarrCustomFormat> CustomFormats,
    int CustomFormatScore,
    bool QualityCutoffNotMet,
    DateTime Date,
    string? DownloadId,
    string EventType,
    Dictionary<string, string> Data);

public sealed record RadarrCommandRequest
{
    public string? Name { get; init; }

    public List<int>? MovieIds { get; init; }

    public int? MovieId { get; init; }
}

public sealed record RadarrCommand(
    int Id,
    string Name,
    string CommandName,
    string? Message,
    Dictionary<string, JsonElement> Body,
    string Priority,
    string Status,
    string Result,
    DateTime Queued,
    DateTime? Started,
    DateTime? Ended,
    string? Duration,
    string Trigger,
    DateTime StateChangeTime,
    bool SendUpdatesToClient,
    bool UpdateScheduledTask);

public sealed record RadarrFileSystemEntry(string Type, string Name, string Path, long Size, DateTime? LastModified);

public sealed record RadarrFileSystem(string? Parent, List<RadarrFileSystemEntry> Directories, List<RadarrFileSystemEntry> Files);

public sealed record RadarrField
{
    public int Order { get; init; }

    public required string Name { get; init; }

    public string? Label { get; init; }

    public string? HelpText { get; init; }

    public JsonElement? Value { get; init; }

    public string Type { get; init; } = "textbox";

    public bool Advanced { get; init; }

    public string Privacy { get; init; } = "normal";

    public string? SelectOptionsProviderAction { get; init; }
}

public sealed record RadarrIndexer
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public required string Implementation { get; init; }

    public string? ImplementationName { get; init; }

    public required string ConfigContract { get; init; }

    public string? InfoLink { get; init; }

    public bool EnableRss { get; init; } = true;

    public bool EnableAutomaticSearch { get; init; } = true;

    public bool EnableInteractiveSearch { get; init; } = true;

    public bool SupportsRss { get; init; } = true;

    public bool SupportsSearch { get; init; } = true;

    public string Protocol { get; init; } = "torrent";

    public int Priority { get; init; } = 25;

    public int DownloadClientId { get; init; }

    public List<int> Tags { get; init; } = [];

    public List<RadarrField> Fields { get; init; } = [];
}

public sealed record RadarrSystemStatus(
    string AppName,
    string InstanceName,
    string Version,
    DateTime BuildTime,
    bool IsDebug,
    bool IsProduction,
    bool IsAdmin,
    bool IsUserInteractive,
    string StartupPath,
    string AppData,
    string OsName,
    string OsVersion,
    bool IsNetCore,
    bool IsLinux,
    bool IsOsx,
    bool IsWindows,
    bool IsDocker,
    string Mode,
    string Branch,
    string DatabaseType,
    string DatabaseVersion,
    string Authentication,
    int MigrationVersion,
    string UrlBase,
    string RuntimeVersion,
    string RuntimeName,
    DateTime StartTime,
    string PackageVersion,
    string PackageAuthor,
    string PackageUpdateMechanism);
