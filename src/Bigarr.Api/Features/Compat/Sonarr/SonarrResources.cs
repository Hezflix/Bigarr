using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;

namespace Bigarr.Api.Features.Compat.Sonarr;

// Sonarr v3 shapes, reduced to what Seerr and Bazarr read plus what makes a record look like Sonarr's. Where Sonarr's
// shape is Radarr's (languages, quality, media info, tags, root folders, commands…), the Radarr record is reused.
// Null members are left out of the JSON; the members Bazarr indexes without a check are never null (see SonarrMapper).

public sealed record SonarrAlternateTitle(string Title, int? SeasonNumber = null);

public sealed record SonarrImage(string CoverType, string Url, string? RemoteUrl);

public sealed record SonarrSeasonStatistics(
    DateTime? NextAiring,
    DateTime? PreviousAiring,
    int EpisodeFileCount,
    int EpisodeCount,
    int TotalEpisodeCount,
    long SizeOnDisk,
    List<string> ReleaseGroups,
    double PercentOfEpisodes);

public sealed record SonarrSeason(int SeasonNumber, bool Monitored, SonarrSeasonStatistics? Statistics = null);

public sealed record SonarrSeriesStatistics(
    int SeasonCount,
    int EpisodeFileCount,
    int EpisodeCount,
    int TotalEpisodeCount,
    long SizeOnDisk,
    List<string> ReleaseGroups,
    double PercentOfEpisodes);

public sealed record SonarrRatings(int Votes, double Value);

public sealed record SonarrAddOptions(bool IgnoreEpisodesWithFiles = false, bool IgnoreEpisodesWithoutFiles = false, string? Monitor = null, bool SearchForMissingEpisodes = false, bool SearchForCutoffUnmetEpisodes = false);

/// <summary>The series resource. <c>id</c> is a series version's id; a lookup hit the key's view does not see has none.</summary>
public sealed record SonarrSeries
{
    public int? Id { get; init; }

    public required string Title { get; init; }

    public List<SonarrAlternateTitle> AlternateTitles { get; init; } = [];

    public required string SortTitle { get; init; }

    public required string Status { get; init; }

    public bool Ended { get; init; }

    public string? Overview { get; init; }

    public DateTime? NextAiring { get; init; }

    public DateTime? PreviousAiring { get; init; }

    public string? Network { get; init; }

    public string? AirTime { get; init; }

    public List<SonarrImage> Images { get; init; } = [];

    public RadarrLanguage? OriginalLanguage { get; init; }

    public string? RemotePoster { get; init; }

    public List<SonarrSeason> Seasons { get; init; } = [];

    public int Year { get; init; }

    public string? Path { get; init; }

    public int QualityProfileId { get; init; }

    public bool SeasonFolder { get; init; }

    public bool Monitored { get; init; }

    public string MonitorNewItems { get; init; } = "all";

    public bool UseSceneNumbering { get; init; }

    public int Runtime { get; init; }

    public int TvdbId { get; init; }

    public int TvRageId { get; init; }

    public int TvMazeId { get; init; }

    public int TmdbId { get; init; }

    public DateTime? FirstAired { get; init; }

    public DateTime? LastAired { get; init; }

    public required string SeriesType { get; init; }

    public string? CleanTitle { get; init; }

    public string? ImdbId { get; init; }

    public required string TitleSlug { get; init; }

    public string? RootFolderPath { get; init; }

    public string? Folder { get; init; }

    public string? Certification { get; init; }

    public List<string> Genres { get; init; } = [];

    public List<int> Tags { get; init; } = [];

    public DateTime Added { get; init; }

    public SonarrRatings? Ratings { get; init; }

    public SonarrSeriesStatistics? Statistics { get; init; }
}

/// <summary>What Seerr posts and puts back: the lookup resource with its own changes. Everything else is ignored.</summary>
public sealed record SonarrSeriesRequest
{
    public int Id { get; init; }

    public int TvdbId { get; init; }

    public string? Title { get; init; }

    public int QualityProfileId { get; init; }

    public List<SonarrSeasonRequest>? Seasons { get; init; }

    public List<int>? Tags { get; init; }

    public bool? SeasonFolder { get; init; }

    public bool Monitored { get; init; } = true;

    public string? MonitorNewItems { get; init; }

    public string? RootFolderPath { get; init; }

    public string? SeriesType { get; init; }

    public SonarrAddOptions? AddOptions { get; init; }
}

public sealed record SonarrSeasonRequest(int SeasonNumber, bool Monitored);

public sealed record SonarrEpisodeFile(
    int Id,
    /// <summary>The compat series id, i.e. the version id.</summary>
    int SeriesId,
    int SeasonNumber,
    string RelativePath,
    string Path,
    long Size,
    DateTime DateAdded,
    string? SceneName,
    string? ReleaseGroup,
    List<RadarrLanguage> Languages,
    RadarrQuality Quality,
    List<RadarrCustomFormat> CustomFormats,
    int CustomFormatScore,
    int IndexerFlags,
    string ReleaseType,
    RadarrMediaInfo? MediaInfo,
    bool QualityCutoffNotMet);

public sealed record SonarrEpisode
{
    /// <summary>The episode's own id, shared by every version of the series.</summary>
    public int Id { get; init; }

    /// <summary>The version the key's view reads the episode through.</summary>
    public int SeriesId { get; init; }

    public int TvdbId { get; init; }

    public int EpisodeFileId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    public required string Title { get; init; }

    public string? AirDate { get; init; }

    public DateTime? AirDateUtc { get; init; }

    public int Runtime { get; init; }

    public string? Overview { get; init; }

    public SonarrEpisodeFile? EpisodeFile { get; init; }

    public bool HasFile { get; init; }

    public bool Monitored { get; init; }

    public int? AbsoluteEpisodeNumber { get; init; }

    public int? SceneAbsoluteEpisodeNumber { get; init; }

    public int? SceneEpisodeNumber { get; init; }

    public int? SceneSeasonNumber { get; init; }

    public bool UnverifiedSceneNumbering { get; init; }

    public DateTime? LastSearchTime { get; init; }

    public List<SonarrImage> Images { get; init; } = [];
}

public sealed record SonarrEpisodesMonitoredRequest(List<int>? EpisodeIds, bool Monitored);

public sealed record SonarrEpisodeRequest(bool Monitored);

/// <summary>The episode of a queue record, as Seerr's download tracker reads it.</summary>
public sealed record SonarrQueueEpisode(int Id, int SeriesId, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, string Title, bool HasFile, bool Monitored);

/// <summary>One record per (download, episode): a season pack is as many records as it has episodes, sharing its download.</summary>
public sealed record SonarrQueueItem(
    int Id,
    int SeriesId,
    int EpisodeId,
    int SeasonNumber,
    SonarrQueueEpisode Episode,
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
    string? OutputPath,
    bool EpisodeHasFile);

public sealed record SonarrHistoryRecord(
    int Id,
    int EpisodeId,
    int SeriesId,
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

public sealed record SonarrCommandRequest
{
    public string? Name { get; init; }

    public int? SeriesId { get; init; }

    public List<int>? SeriesIds { get; init; }

    public int? SeasonNumber { get; init; }

    public List<int>? EpisodeIds { get; init; }

    /// <summary>Everything the client sent, echoed back in the command's <c>body</c> as Sonarr does.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
