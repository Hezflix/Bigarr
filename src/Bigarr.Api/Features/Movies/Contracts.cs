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
    /// <summary>Search the indexers right away, in the background (blueprint §5: "si monitored et disponibilité atteinte, recherche immédiate").</summary>
    bool SearchOnAdd = false);

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
    bool SearchOnAdd = false);

public sealed record UpdateVersionRequest(
    string? Name = null,
    VersionMode? Mode = null,
    int? QualityProfileId = null,
    bool? Monitored = null,
    MinimumAvailability? MinimumAvailability = null,
    ConstraintsDto? Constraints = null,
    bool ClearConstraints = false);

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
    MediaInfo? MediaInfo);

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
    bool? CutoffMet = null);

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
    string? Quality);

public sealed record MovieSummary(
    int Id,
    int TmdbId,
    string Title,
    string SortTitle,
    int? Year,
    string? PosterUrl,
    MovieStatus EffectiveStatus,
    bool Monitored,
    List<VersionSummary> Versions);
