using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.SeriesFilms;

/// <summary>The series page's "Films in this series": candidates and linked films, with the auto-add setting.</summary>
public sealed record SeriesFilmsResponse(bool AutoAdd, List<SeriesFilmResponse> Films);

/// <param name="Label"><c>S00E02</c>.</param>
/// <param name="ExistingMovieId">The film is already in the library (not linked yet): adding links to it.</param>
public sealed record SeriesFilmResponse(
    int EpisodeId,
    int SeasonNumber,
    int EpisodeNumber,
    string Label,
    string Title,
    DateOnly? AirDate,
    int? Runtime,
    FilmConfidence? Confidence,
    List<string> Evidence,
    int? TmdbId,
    string? ImdbId,
    int? AfterSeason,
    int? BeforeSeason,
    FilmPlacementSource PlacementSource,
    string? PlacementLabel,
    int? ExistingMovieId,
    SeriesFilmLinkResponse? Link);

public sealed record SeriesFilmLinkResponse(
    int Id,
    int MovieId,
    string MovieTitle,
    int? MovieYear,
    bool MovieHasFile,
    bool AutoAdded,
    DateTime CreatedAt,
    string? LinkError,
    List<SeriesFilmVersionLinkResponse> Versions);

/// <param name="Linked">The special is a hardlink of the film's file in this version's folder.</param>
public sealed record SeriesFilmVersionLinkResponse(int VersionId, string VersionName, bool Monitored, bool Linked, string? RelativePath);

/// <param name="QualityProfileId">Defaults to the default film profile (the system <c>1080p</c>).</param>
/// <param name="RootFolderId">Defaults to the first film root folder.</param>
/// <param name="TmdbId">For a special detection found no TMDB id for: the user says which film it is.</param>
public sealed record AddSeriesFilmRequest(int EpisodeId, int? QualityProfileId = null, int? RootFolderId = null, bool SearchOnAdd = true, int? TmdbId = null);

/// <summary>On the film page: "Also special S00E02 of Show (after season 1)".</summary>
public sealed record MovieSeriesLinkResponse(int SeriesId, string SeriesTitle, int EpisodeId, string Label, int? AfterSeason, int? BeforeSeason, string? PlacementLabel);

public sealed record SeriesFilmSettingsRequest(bool AutoAdd);
