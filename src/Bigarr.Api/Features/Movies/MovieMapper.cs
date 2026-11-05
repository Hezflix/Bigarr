using Bigarr.Api.Infrastructure.Decision;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Movies;

public static class MovieMapper
{
    public static MovieResponse ToResponse(Movie movie, IReadOnlySet<int> activeTargets, DateOnly today, IReadOnlyDictionary<int, FileAssessment>? assessments = null) => new(
        movie.Id,
        movie.TmdbId,
        movie.ImdbId,
        movie.Title,
        movie.OriginalTitle,
        movie.SortTitle,
        movie.Year,
        movie.Runtime,
        movie.Overview,
        movie.Certification,
        movie.OriginalLanguage,
        movie.Genres,
        movie.Ratings,
        movie.AlternativeTitles,
        movie.TmdbStatus,
        Availability.EffectiveStatus(movie, today),
        movie.InCinemasDate,
        movie.DigitalReleaseDate,
        movie.PhysicalReleaseDate,
        movie.CollectionTmdbId,
        Image(movie, ImageType.Poster),
        Image(movie, ImageType.Fanart),
        movie.DefaultRootFolderId,
        movie.AddedAt,
        movie.LastMetadataRefreshAt,
        movie.Monitored,
        movie.Versions
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Id)
            .Select(v => ToVersion(v, activeTargets.Contains(v.Id), today, assessments is not null && assessments.TryGetValue(v.Id, out var a) ? a : null))
            .ToList());

    public static VersionResponse ToVersion(MovieVersion version, bool hasActiveGrab, DateOnly today, FileAssessment? assessment = null)
    {
        var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
        return new VersionResponse(
            version.Id,
            version.Name,
            version.Mode,
            version.QualityProfileId,
            version.QualityProfile.Name,
            version.IsDefault,
            version.Monitored,
            version.RootFolderId,
            version.RootFolder.Path,
            version.FolderName,
            folder,
            version.MinimumAvailability,
            ToConstraints(version.Constraints),
            Availability.StatusOf(version, hasActiveGrab, today),
            Availability.IsAvailable(version.Movie, version.MinimumAvailability, today),
            version.File is null ? null : ToFile(version.File, folder),
            version.CreatedAt,
            assessment?.CustomFormats.ToList(),
            assessment?.CustomFormatScore,
            assessment?.CutoffMet,
            version.Tags.Select(t => t.Id).OrderBy(id => id).ToList());
    }

    public static MediaFileResponse ToFile(MediaFile file, string folder) => new(
        file.Id,
        file.RelativePath,
        Path.Combine(folder, file.RelativePath),
        file.Size,
        file.DateAdded,
        file.QualityId,
        file.Quality?.Name ?? $"#{file.QualityId}",
        file.Revision.Version,
        file.Revision.IsRepack,
        file.Edition,
        file.ReleaseGroup,
        file.Languages,
        file.OriginalReleaseTitle,
        file.MediaInfo is not null,
        file.MediaInfo);

    public static ConstraintsDto? ToConstraints(AcquisitionConstraints? constraints) =>
        constraints is null || constraints.IsEmpty
            ? null
            : new ConstraintsDto(constraints.Edition, constraints.RequiredCustomFormatIds, constraints.ForbiddenCustomFormatIds, constraints.ReleaseGroup);

    public static AcquisitionConstraints? FromConstraints(ConstraintsDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var constraints = new AcquisitionConstraints
        {
            Edition = string.IsNullOrWhiteSpace(dto.Edition) ? null : dto.Edition.Trim(),
            ReleaseGroup = string.IsNullOrWhiteSpace(dto.ReleaseGroup) ? null : dto.ReleaseGroup.Trim(),
            RequiredCustomFormatIds = dto.RequiredCustomFormatIds?.Distinct().ToList() ?? [],
            ForbiddenCustomFormatIds = dto.ForbiddenCustomFormatIds?.Distinct().ToList() ?? [],
        };
        return constraints.IsEmpty ? null : constraints;
    }

    private static string? Image(Movie movie, ImageType type) =>
        movie.Images.FirstOrDefault(i => i.Type == type)?.RemoteUrl;
}
