namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Library;
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
    List<int>? TagIds = null,
    /// <summary>An existing folder in the root (library import); defaults to "Title (Year)".</summary>
    string? FolderName = null);

public sealed record AddSeriesVersionRequest(
    string Name,
    int QualityProfileId,
    /// <summary>Defaults to the series' default root folder.</summary>
    int? RootFolderId = null,
    /// <summary>Defaults to the series folder template (Settings → Media management).</summary>
    string? FolderName = null,
    MonitoringOption Monitor = MonitoringOption.All,
    bool SeasonFolder = true,
    NewItemMonitoring MonitorNewItems = NewItemMonitoring.All,
    bool Monitored = true,
    List<int>? TagIds = null,
    /// <summary>Same constraints as a film version's: edition pattern (matched on the release title for series), required/forbidden custom formats, release group.</summary>
    Bigarr.Api.Features.Movies.ConstraintsDto? Constraints = null);

public sealed record UpdateSeriesRequest(SeriesType? SeriesType = null);

public sealed record UpdateSeriesVersionRequest(
    string? Name = null,
    int? QualityProfileId = null,
    bool? Monitored = null,
    bool? SeasonFolder = null,
    NewItemMonitoring? MonitorNewItems = null,
    /// <summary>The full list of tag ids; null leaves the tags untouched.</summary>
    List<int>? TagIds = null,
    /// <summary>Replaces the constraints; null leaves them, <see cref="ClearConstraints"/> removes them.</summary>
    Bigarr.Api.Features.Movies.ConstraintsDto? Constraints = null,
    bool ClearConstraints = false);

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

/// <param name="Status">
/// The library chip, as a film version's: downloading (an active grab on one of its episodes), missing (a monitored
/// episode has aired without a file), available (files and nothing missing), pending (nothing aired to fetch yet).
/// </param>
/// <param name="Qualities">The qualities of its files, best first.</param>
/// <param name="CutoffMet">Null without a file; false when a file's quality is below the profile cutoff.</param>
public sealed record SeriesVersionSummary(
    int Id,
    string Name,
    bool IsDefault,
    bool Monitored,
    int QualityProfileId,
    string QualityProfile,
    EpisodeStatistics Statistics,
    VersionMode Mode = VersionMode.Managed,
    int RootFolderId = 0,
    List<int>? Tags = null,
    NewItemMonitoring MonitorNewItems = NewItemMonitoring.All,
    VersionStatus Status = VersionStatus.Pending,
    List<string>? Qualities = null,
    bool? CutoffMet = null);

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
    EpisodeStatistics Statistics,
    Bigarr.Api.Features.Movies.ConstraintsDto? Constraints = null);

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
    List<SeriesVersionResponse> Versions,
    /// <summary>The TMDB episode group when <see cref="Ordering"/> is one.</summary>
    string? EpisodeGroupId = null,
    string? EpisodeGroupName = null,
    EpisodeGroupType? EpisodeGroupType = null,
    /// <summary>Episodes of the order TVDB has no counterpart for: listed, not tracked.</summary>
    List<OrderOnlyEpisode>? OrderOnlyEpisodes = null);

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
    List<EpisodeVersionState> Versions,
    /// <summary>Numbers in the series' alternative order; null in the TVDB order or when the order lacks the episode.</summary>
    int? OrderSeasonNumber = null,
    int? OrderEpisodeNumber = null,
    int? OrderAbsoluteNumber = null,
    /// <summary>What the page shows and the files are named after: the order's numbers, else TVDB's.</summary>
    int DisplaySeasonNumber = 0,
    int DisplayEpisodeNumber = 0,
    int? DisplayAbsoluteNumber = null,
    /// <summary>The series shows another order and this episode has no counterpart there (its file stays as named).</summary>
    bool NotInOrder = false,
    /// <summary>The special is a film of the library (S8): the film engine owns it ("owned by film X").</summary>
    EpisodeFilmOwner? OwnedByFilm = null,
    /// <summary>The scene numbering TheXEM maps this episode to, when it differs from TVDB's (null otherwise).</summary>
    int? SceneSeasonNumber = null,
    int? SceneEpisodeNumber = null,
    int? SceneAbsoluteNumber = null);

/// <summary>The film that owns a special: searched, grabbed and upgraded as a film; the special is its hardlink.</summary>
public sealed record EpisodeFilmOwner(int MovieId, string Title, int? Year);

/// <summary>A file of one version, with the episodes it holds (several for a multi-episode file).</summary>
public sealed record EpisodeFileResponse(
    int Id,
    int VersionId,
    string RelativePath,
    string Path,
    int SeasonNumber,
    long Size,
    DateTime DateAdded,
    int QualityId,
    string Quality,
    int RevisionVersion,
    bool IsRepack,
    string? ReleaseGroup,
    List<string> Languages,
    string? OriginalReleaseTitle,
    MediaInfo? MediaInfo,
    List<int> EpisodeIds,
    /// <summary>"S01E01", "S01E02-E03": the episodes in TVDB numbering.</summary>
    string Episodes,
    /// <summary>A hardlink of a linked film's file (S8): the film engine owns it.</summary>
    string? LinkedFromPath = null,
    /// <summary>The quality's resolution (0 when unknown), what the season tables sort the quality on.</summary>
    int QualityResolution = 0,
    Bigarr.Domain.Acquisition.IndexerFlags IndexerFlags = Bigarr.Domain.Acquisition.IndexerFlags.None,
    /// <summary>The custom formats the file matches, and the score its version's profile gives them.</summary>
    List<string>? CustomFormats = null,
    int CustomFormatScore = 0);

public sealed record UnmatchedEpisodeFileResponse(
    int Id,
    int VersionId,
    string RelativePath,
    string Path,
    long Size,
    UnmatchedFileReason Reason,
    string? Detail,
    DateTime DiscoveredAt,
    DateTime LastSeenAt);

public sealed record SeriesFilesResponse(List<EpisodeFileResponse> Files, List<UnmatchedEpisodeFileResponse> Unmatched);

/// <summary>Maps an unmapped folder of a series root to a show: a library series (<see cref="SeriesId"/>) or one to add.</summary>
public sealed record MapSeriesFolderRequest(int? SeriesId = null, int? TvdbId = null, int? TmdbId = null, int? QualityProfileId = null);

public sealed record MapSeriesFolderResponse(int SeriesId, int VersionId, ScanStatusResponse Scan);
