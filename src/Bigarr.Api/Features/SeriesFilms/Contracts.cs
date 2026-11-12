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

// ---- the review page: every candidate, and what linking it would do on disk -----------------------------------------

/// <summary>Where a candidate stands: linked to a film, waiting for a decision, or set aside by the user.</summary>
public enum SeriesFilmState
{
    Suggested,
    Linked,
    Dismissed,
}

/// <summary>One thing linking does (or the keeper would do next, for a linked special). Nothing here has run.</summary>
public enum FilmFileStepKind
{
    /// <summary>Shadow mode: the link is recorded, no file is touched.</summary>
    ShadowNoFiles,

    /// <summary>The film is created in the library (<c>To</c>: its folder).</summary>
    AddFilm,

    /// <summary>The film is already in the library: it is reused as it is.</summary>
    ReuseFilm,

    /// <summary>Neither side has a file: the film engine searches the film.</summary>
    SearchFilm,

    /// <summary>Neither side has a file and the film is already in the library: it keeps waiting for its own search.</summary>
    NothingOnDisk,

    /// <summary>The special's file becomes the film's file: moved (a rename, never a copy) from <c>From</c> into <c>To</c>.</summary>
    AdoptSpecialFile,

    /// <summary>The film's file is hardlinked into the show (<c>To</c>), in <c>Version</c>.</summary>
    HardlinkIntoSpecials,

    /// <summary>Both have a file: the film's wins, the special's (<c>From</c>) goes to the recycle bin.</summary>
    FilmFileWins,

    /// <summary>The special is already the hardlink of the film's file in <c>Version</c>.</summary>
    AlreadyHardlinked,

    /// <summary>The special's file also holds other episodes (<c>From</c>): left as is, not linked.</summary>
    SharedFileLeft,

    /// <summary>The film's file is gone, or the version stopped wanting the special: the hardlink <c>From</c> is removed.</summary>
    RemoveHardlink,

    /// <summary>The last attempt failed (<c>Note</c>); nothing was copied.</summary>
    LinkError,
}

public sealed record FilmFileStep(FilmFileStepKind Kind, string? Version = null, string? From = null, string? To = null, string? Note = null);

/// <param name="MovieId">The film of the library: linked, or already there with that TMDB id.</param>
/// <param name="SpecialHasFile">A file (its own or the hardlink) in any version of the show.</param>
/// <param name="FilmHasFile">The film's default version has its file on disk.</param>
public sealed record SeriesFilmReviewRow(
    int SeriesId,
    string SeriesTitle,
    int EpisodeId,
    string Label,
    string Title,
    int? Runtime,
    FilmConfidence? Confidence,
    List<string> Evidence,
    string? PlacementLabel,
    int? TmdbId,
    string? FilmTitle,
    int? FilmYear,
    int? MovieId,
    bool FilmInLibrary,
    SeriesFilmState State,
    bool SpecialHasFile,
    bool FilmHasFile,
    string? LinkError,
    List<FilmFileStep> Plan);

public sealed record SeriesFilmReviewResponse(bool ShadowEnabled, bool AutoAdd, List<SeriesFilmReviewRow> Rows);

/// <summary>The library toolbars' badge: candidates nobody decided on yet.</summary>
public sealed record SeriesFilmSummaryResponse(int Suggested, int Linked, int Dismissed);

public sealed record LinkSeriesFilmsRequest(List<int> EpisodeIds);

public sealed record LinkSeriesFilmResult(int EpisodeId, bool Linked, string? Error);
