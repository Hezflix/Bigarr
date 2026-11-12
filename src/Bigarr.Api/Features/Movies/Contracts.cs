using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Movies;

public sealed record MovieLookupResult(
    int TmdbId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    string? PosterUrl,
    MovieStatus Status,
    /// <summary>Set when the film is already in the library.</summary>
    int? ExistingMovieId,
    bool Excluded);

public sealed record AddMovieRequest(
    int TmdbId,
    int QualityProfileId,
    int RootFolderId,
    /// <summary>Name of the default version. Defaults to the profile's slug.</summary>
    string? VersionName = null,
    bool Monitored = true,
    MinimumAvailability MinimumAvailability = MinimumAvailability.Released,
    /// <summary>Search the indexers right away, in the background ("si monitored et disponibilité atteinte, recherche immédiate").</summary>
    bool SearchOnAdd = false,
    /// <summary>Tags of the default version.</summary>
    List<int>? TagIds = null);

public sealed record ConstraintsDto(
    string? Edition,
    List<int>? RequiredCustomFormatIds,
    List<int>? ForbiddenCustomFormatIds,
    string? ReleaseGroup);

public sealed record AddVersionRequest(
    string Name,
    VersionMode Mode,
    int QualityProfileId,
    /// <summary>Defaults to the movie's default root folder.</summary>
    int? RootFolderId = null,
    /// <summary>Defaults to the default version's folder name.</summary>
    string? FolderName = null,
    bool Monitored = true,
    MinimumAvailability MinimumAvailability = MinimumAvailability.Released,
    ConstraintsDto? Constraints = null,
    bool SearchOnAdd = false,
    List<int>? TagIds = null);

public sealed record UpdateVersionRequest(
    string? Name = null,
    VersionMode? Mode = null,
    int? QualityProfileId = null,
    bool? Monitored = null,
    MinimumAvailability? MinimumAvailability = null,
    ConstraintsDto? Constraints = null,
    bool ClearConstraints = false,
    /// <summary>The full list of tag ids; null leaves the tags untouched.</summary>
    List<int>? TagIds = null);

public sealed record MediaFileResponse(
    int Id,
    string RelativePath,
    string Path,
    long Size,
    DateTime DateAdded,
    int QualityId,
    string Quality,
    int RevisionVersion,
    bool IsRepack,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    string? OriginalReleaseTitle,
    bool HasMediaInfo,
    MediaInfo? MediaInfo,
    /// <summary>Side files imported with it. Files written by others (Bazarr) are not listed.</summary>
    List<ExtraFileResponse> Extras);

public sealed record ExtraFileResponse(
    int Id,
    string RelativePath,
    string Path,
    string Extension,
    ExtraFileKind Kind,
    /// <summary>ISO 639-1 code, null when the name did not say.</summary>
    string? Language,
    bool Forced,
    bool HearingImpaired,
    DateTime DateAdded);

public sealed record VersionResponse(
    int Id,
    string Name,
    VersionMode Mode,
    int QualityProfileId,
    string QualityProfile,
    bool IsDefault,
    bool Monitored,
    int RootFolderId,
    string RootFolderPath,
    string FolderName,
    string Path,
    MinimumAvailability MinimumAvailability,
    ConstraintsDto? Constraints,
    VersionStatus Status,
    bool IsAvailable,
    MediaFileResponse? File,
    DateTime CreatedAt,
    /// <summary>Custom formats the file matches, judged at read time; null without a file.</summary>
    List<string>? CustomFormats = null,
    int? CustomFormatScore = null,
    /// <summary>Quality at the profile cutoff and score at the cutoff format score: nothing more is wanted.</summary>
    bool? CutoffMet = null,
    List<int>? Tags = null);

public sealed record MovieResponse(
    int Id,
    int TmdbId,
    string? ImdbId,
    string Title,
    string? OriginalTitle,
    string SortTitle,
    int? Year,
    int? Runtime,
    string? Overview,
    string? Certification,
    string? OriginalLanguage,
    List<string> Genres,
    MovieRatings? Ratings,
    List<AlternativeTitle> AlternativeTitles,
    MovieStatus TmdbStatus,
    MovieStatus EffectiveStatus,
    DateOnly? InCinemasDate,
    DateOnly? DigitalReleaseDate,
    DateOnly? PhysicalReleaseDate,
    int? CollectionTmdbId,
    string? PosterUrl,
    string? FanartUrl,
    int DefaultRootFolderId,
    DateTime AddedAt,
    DateTime? LastMetadataRefreshAt,
    bool Monitored,
    List<VersionResponse> Versions);

public sealed record VersionSummary(
    int Id,
    string Name,
    VersionMode Mode,
    bool IsDefault,
    bool Monitored,
    int QualityProfileId,
    VersionStatus Status,
    string? Quality,
    MinimumAvailability MinimumAvailability,
    int RootFolderId,
    List<int> Tags,
    /// <summary>Bytes; null without a file.</summary>
    long? Size,
    /// <summary>Judged now like on the movie page; null without a file.</summary>
    bool? CutoffMet,
    /// <summary>Plays recorded on this version (playback), and when the last one ended.</summary>
    int Plays = 0,
    DateTime? LastPlayedAt = null);

public sealed record MovieSummary(
    int Id,
    int TmdbId,
    string Title,
    string SortTitle,
    int? Year,
    string? PosterUrl,
    MovieStatus EffectiveStatus,
    bool Monitored,
    List<VersionSummary> Versions,
    DateTime AddedAt,
    double? TmdbRating,
    double? ImdbRating,
    /// <summary>Every play of the film, those whose version could not be told included.</summary>
    int Plays = 0,
    DateTime? LastPlayedAt = null);

public enum TagsMode
{
    Add = 1,
    Remove = 2,
    Replace = 3,
}

/// <param name="Versions">Which version of each film: <c>default</c> (when absent), <c>all</c> or <c>version:&lt;name&gt;</c>.</param>
public sealed record BulkUpdateRequest(
    List<int> MovieIds,
    string? Versions = null,
    bool? Monitored = null,
    int? QualityProfileId = null,
    MinimumAvailability? MinimumAvailability = null,
    List<int>? TagIds = null,
    /// <summary>How <see cref="TagIds"/> combine with the version's tags; adds when absent.</summary>
    TagsMode? ApplyTags = null);

public sealed record BulkSearchRequest(List<int> MovieIds, string? Versions = null);

public sealed record BulkDeleteRequest(
    List<int> MovieIds,
    /// <summary><c>all</c> deletes whole films.</summary>
    string? Versions = null,
    bool DeleteFiles = false,
    /// <summary>Only for the films that go away entirely.</summary>
    bool AddImportExclusion = false,
    /// <summary>Answers what would happen and changes nothing: the confirmation reads it.</summary>
    bool DryRun = false);

public enum BulkOutcome
{
    Updated = 1,
    Unchanged = 2,
    Queued = 3,
    AlreadyQueued = 4,
    MovieDeleted = 5,
    VersionsDeleted = 6,
    Skipped = 7,
    Failed = 8,

    /// <summary>Series bulk delete: the series left the library with its last version.</summary>
    SeriesDeleted = 9,
}

/// <param name="Versions">Names of the versions acted on.</param>
/// <param name="Detail">Why the film was skipped or failed.</param>
/// <param name="Files">Files removed with the versions (delete with files).</param>
/// <param name="Promoted">The version that became the default because the default one was deleted.</param>
public sealed record BulkMovieResult(int MovieId, string Title, BulkOutcome Outcome, List<string> Versions, string? Detail = null, int Files = 0, string? Promoted = null);

public sealed record BulkResponse(List<BulkMovieResult> Results);

/// <param name="RecycleBin">Where deleted files go; null when they are deleted for good.</param>
public sealed record BulkDeleteResponse(bool DryRun, string? RecycleBin, List<BulkMovieResult> Results);
