using Bigarr.Domain.Library;
using Bigarr.Domain.Naming;

namespace Bigarr.Api.Features.Import;

public sealed record SeriesNamingTokenResponse(string Token, string Description, string Example, SeriesTokenScope Scope);

/// <summary>Sonarr's episode naming, as Settings → Media management shows it.</summary>
public sealed record SeriesNamingResponse(
    bool RenameEpisodes,
    string SeriesFolderTemplate,
    string SeasonFolderTemplate,
    string SpecialsFolderName,
    string StandardEpisodeTemplate,
    string DailyEpisodeTemplate,
    string AnimeEpisodeTemplate,
    MultiEpisodeStyle MultiEpisodeStyle,
    SeriesNamingPreviewResponse Preview,
    List<SeriesNamingTokenResponse> Tokens);

public sealed record SeriesNamingRequest(
    bool RenameEpisodes,
    string SeriesFolderTemplate,
    string SeasonFolderTemplate,
    string SpecialsFolderName,
    string StandardEpisodeTemplate,
    string DailyEpisodeTemplate,
    string AnimeEpisodeTemplate,
    MultiEpisodeStyle MultiEpisodeStyle);

/// <summary>Draft templates; a null one is not previewed. Used while typing.</summary>
public sealed record SeriesNamingPreviewRequest(
    string? SeriesFolderTemplate = null,
    string? SeasonFolderTemplate = null,
    string? SpecialsFolderName = null,
    string? StandardEpisodeTemplate = null,
    string? DailyEpisodeTemplate = null,
    string? AnimeEpisodeTemplate = null,
    MultiEpisodeStyle MultiEpisodeStyle = MultiEpisodeStyle.PrefixedRange);

/// <summary>Each template rendered on Sonarr's sample series, with the errors that would refuse it.</summary>
public sealed record SeriesNamingPreviewResponse(
    string? SeriesFolder,
    string? SeasonFolder,
    string? SpecialsFolder,
    string? StandardEpisode,
    string? MultiEpisode,
    string? DailyEpisode,
    string? AnimeEpisode,
    Dictionary<string, List<string>> Errors);

// ---- manual import ----------------------------------------------------------------------------------------------------

public sealed record SeriesManualImportEpisode(int Id, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, string Title, bool HasFile);

public sealed record SeriesManualImportVersion(int Id, string Name, bool IsDefault);

/// <param name="Rejections">Why the import would not take it on its own (not an upgrade, not mapped); the user can force it.</param>
public sealed record SeriesManualImportFileResponse(
    string Path,
    string RelativePath,
    string Name,
    long Size,
    bool IsSample,
    int QualityId,
    string Quality,
    int RevisionVersion,
    string? ReleaseGroup,
    List<string> Languages,
    int? SeriesId,
    string? SeriesTitle,
    int? VersionId,
    List<SeriesManualImportVersion> Versions,
    int? SeasonNumber,
    List<SeriesManualImportEpisode> Episodes,
    /// <summary>Why no series or episode was found; null when the mapper proposes episodes.</summary>
    string? MatchReason,
    List<string> Rejections,
    List<ManualImportExtraResponse> Extras);

public sealed record SeriesManualImportResponse(
    string Path,
    int? GrabId,
    string? ReleaseTitle,
    TransferMode DefaultTransferMode,
    List<SeriesManualImportFileResponse> Files);

public sealed record SeriesManualImportItemRequest(string Path, int VersionId, List<int> EpisodeIds, int? QualityId = null, int? GrabId = null, List<string>? ExtraPaths = null);

public sealed record SeriesManualImportRequest(List<SeriesManualImportItemRequest> Files, TransferMode? TransferMode = null);

public sealed record SeriesManualImportResultResponse(string Path, int VersionId, bool Imported, string? Reason, int? EpisodeFileId, string? DestinationPath, bool Upgraded, List<string> Episodes);

/// <summary>Attach a file the scan could not place to episodes, where it lies. <paramref name="Replace"/> sends a file already holding one of them to the recycle bin.</summary>
public sealed record AssignUnmatchedRequest(List<int> EpisodeIds, bool Rename = false, bool Replace = false, int? QualityId = null);

// ---- rename -------------------------------------------------------------------------------------------------------------

public sealed record SeriesRenamePreviewResponse(
    int VersionId,
    string VersionName,
    int EpisodeFileId,
    int SeasonNumber,
    List<string> Episodes,
    string CurrentRelativePath,
    string NewRelativePath,
    bool Changed,
    List<ExtraRenamePreviewResponse> Extras);

public sealed record SeriesRenameRequest(int SeriesId, int? VersionId = null, int? SeasonNumber = null, List<int>? EpisodeFileIds = null);

public sealed record SeriesRenameResultResponse(int EpisodeFileId, string RelativePath, bool Renamed, string? Error);

// ---- episode files ------------------------------------------------------------------------------------------------------

public sealed record EpisodeExtraResponse(int Id, string RelativePath, string Extension, Bigarr.Domain.Movies.ExtraFileKind Kind, string? Language, bool Forced, bool HearingImpaired);

/// <summary>An episode file with what the "info" action shows.</summary>
public sealed record EpisodeFileDetailResponse(
    int Id,
    int VersionId,
    string VersionName,
    string RelativePath,
    string Path,
    long Size,
    DateTime DateAdded,
    string Quality,
    int RevisionVersion,
    string? ReleaseGroup,
    List<string> Languages,
    string? OriginalReleaseTitle,
    Bigarr.Domain.Movies.MediaInfo? MediaInfo,
    List<string> CustomFormats,
    int CustomFormatScore,
    List<SeriesManualImportEpisode> Episodes,
    List<EpisodeExtraResponse> Extras);
