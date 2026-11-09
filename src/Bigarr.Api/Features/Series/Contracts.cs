namespace Bigarr.Api.Features.Series;

using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;

public sealed record SeriesLookupResult(
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    SeriesStatus Status,
    string? Network,
    /// <summary>A hint for the add form: Japanese animation → anime, talk shows → daily.</summary>
    SeriesType SuggestedType,
    /// <summary>Set when the show is already in the library.</summary>
    int? ExistingSeriesId);

public sealed record AddSeriesRequest(
    int QualityProfileId,
    int RootFolderId,
    /// <summary>The key; <see cref="TmdbId"/> alone works for a show TVDB lacks.</summary>
    int? TvdbId = null,
    int? TmdbId = null,
    /// <summary>Defaults to the provider's suggestion.</summary>
    SeriesType? SeriesType = null,
    MonitoringOption Monitor = MonitoringOption.All,
    bool SeasonFolder = true,
    NewItemMonitoring MonitorNewItems = NewItemMonitoring.All,
    /// <summary>Name of the default version. Defaults to the profile's slug.</summary>
    string? VersionName = null,
    bool Monitored = true,
    List<int>? TagIds = null);

public sealed record AddSeriesVersionRequest(
    string Name,
    int QualityProfileId,
    /// <summary>Defaults to the series' default root folder.</summary>
    int? RootFolderId = null,
    /// <summary>Defaults to the default version's folder name.</summary>
    string? FolderName = null,
    MonitoringOption Monitor = MonitoringOption.All,
    bool SeasonFolder = true,
    NewItemMonitoring MonitorNewItems = NewItemMonitoring.All,
    bool Monitored = true,
    List<int>? TagIds = null);

public sealed record UpdateSeriesRequest(SeriesType? SeriesType = null);

public sealed record UpdateSeriesVersionRequest(
    string? Name = null,
    int? QualityProfileId = null,
    bool? Monitored = null,
    bool? SeasonFolder = null,
    NewItemMonitoring? MonitorNewItems = null,
    /// <summary>The full list of tag ids; null leaves the tags untouched.</summary>
    List<int>? TagIds = null);

public sealed record SeasonMonitoringRequest(bool Monitored);

public sealed record EpisodeMonitoringRequest(List<int> EpisodeIds, bool Monitored);

public sealed record ApplyMonitoringRequest(MonitoringOption Monitor);

/// <summary>Sonarr's statistics, per version: files and monitoring belong to a version, the episode list does not.</summary>
public sealed record EpisodeStatistics(
    /// <summary>Monitored episodes that have aired, plus episodes with a file (Sonarr's <c>episodeCount</c>).</summary>
    int EpisodeCount,
    int EpisodeFileCount,
    int MonitoredEpisodeCount,
    /// <summary>Every episode outside the specials, aired or not.</summary>
    int TotalEpisodeCount,
    long SizeOnDisk)
{
    public static readonly EpisodeStatistics Empty = new(0, 0, 0, 0, 0);
}

public sealed record SeriesVersionSummary(
    int Id,
    string Name,
    bool IsDefault,
    bool Monitored,
    int QualityProfileId,
    string QualityProfile,
    EpisodeStatistics Statistics);

public sealed record SeriesSummary(
    int Id,
    int? TvdbId,
    int? TmdbId,
    string Title,
    string SortTitle,
    int? Year,
    SeriesStatus Status,
    string? Network,
    SeriesType SeriesType,
    string? PosterUrl,
    DateTime AddedAt,
    bool Monitored,
    /// <summary>Regular seasons (specials excluded).</summary>
    int SeasonCount,
    DateOnly? NextAiring,
    List<SeriesVersionSummary> Versions);

public sealed record SeriesVersionResponse(
    int Id,
    string Name,
    VersionMode Mode,
    int QualityProfileId,
    string QualityProfile,
    bool IsDefault,
    bool Monitored,
    bool SeasonFolder,
    NewItemMonitoring MonitorNewItems,
    int RootFolderId,
    string RootFolderPath,
    string FolderName,
    string Path,
    DateTime CreatedAt,
    List<int> Tags,
    EpisodeStatistics Statistics);

public sealed record SeasonVersionState(int VersionId, bool Monitored, EpisodeStatistics Statistics);

public sealed record SeasonResponse(
    int SeasonNumber,
    string? Title,
    string? Overview,
    string? PosterUrl,
    int EpisodeCount,
    int AiredEpisodeCount,
    List<SeasonVersionState> Versions);

public sealed record SeriesResponse(
    int Id,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string Title,
    string? OriginalTitle,
    string SortTitle,
    int? Year,
    string? Overview,
    SeriesStatus Status,
    string? Network,
    int? Runtime,
    string? AirTime,
    string? OriginalCountry,
    string? OriginalLanguage,
    SeriesType SeriesType,
    EpisodeOrder Ordering,
    List<string> Genres,
    List<AlternativeTitle> AlternativeTitles,
    string? PosterUrl,
    string? FanartUrl,
    DateOnly? FirstAired,
    DateOnly? LastAired,
    int DefaultRootFolderId,
    DateTime AddedAt,
    DateTime? LastMetadataRefreshAt,
    bool Monitored,
    List<SeasonResponse> Seasons,
    List<SeriesVersionResponse> Versions);

public sealed record EpisodeVersionState(int VersionId, int TargetId, bool Monitored, bool HasFile, int? EpisodeFileId);

public sealed record EpisodeResponse(
    /// <summary>Shared by every version of the series.</summary>
    int Id,
    int? TvdbId,
    int? TmdbId,
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber,
    DateOnly? AirDate,
    DateTime? AirDateUtc,
    string Title,
    string? Overview,
    int? Runtime,
    string? ImageUrl,
    bool IsSpecial,
    bool HasAired,
    List<EpisodeVersionState> Versions);
