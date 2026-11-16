using Bigarr.Api.Features.Requests;
using Bigarr.Domain.Discover;

namespace Bigarr.Api.Features.Discover;

// Everything here reaches every signed-in user: TMDB's catalogue plus an availability state per version. Nothing of the
// library itself (paths, sizes, releases, indexers, profiles, root folders); only the admin gets the library id, for
// the link to the title's library page.

/// <summary>"Standard" is the default version; "4K" is null when Discover shows the default version only.</summary>
public sealed record AvailabilityResponse(AvailabilityState Standard, AvailabilityState? FourK);

public enum DiscoverMediaType
{
    Movie,
    Series,
    Person,
}

/// <param name="Subtitle">A person's department; in a person's credits, the role or the job.</param>
/// <param name="Availability">Null for a person.</param>
/// <param name="Requests">Active requests per slot (who asked is not said); null for a person or when nobody asked.</param>
public sealed record DiscoverItem(
    DiscoverMediaType MediaType,
    int TmdbId,
    string Title,
    int? Year,
    string? ReleaseDate,
    string? PosterUrl,
    string? Overview,
    double? VoteAverage,
    string? Subtitle,
    AvailabilityResponse? Availability,
    RequestSlotsResponse? Requests = null);

public sealed record DiscoverPage(int Page, int TotalPages, int TotalResults, List<DiscoverItem> Results);

public sealed record DiscoverTile(int Id, string Name);

/// <param name="Title">Null for a built-in row: the page names it in the reader's language.</param>
/// <param name="Tiles">Genre, studio or network tiles for those rows; null for a row of titles.</param>
public sealed record DiscoverSliderResponse(int Id, DiscoverSliderKind Kind, string? Title, string? Value, List<DiscoverTile>? Tiles);

/// <param name="Configured">False without a TMDB API key: there is nothing to browse.</param>
/// <param name="FourK">Whether availability carries a 4K state next to the standard one.</param>
public sealed record DiscoverHomeResponse(bool Configured, bool FourK, List<DiscoverSliderResponse> Sliders);

public sealed record DiscoverRef(int Id, string Name);

public sealed record DiscoverCastMember(int Id, string Name, string? Character, string? ProfileUrl);

/// <param name="LibraryId">The film's id in the library, for the admin only (null for everyone else and when absent).</param>
public sealed record DiscoverMovieResponse(
    int TmdbId,
    string? ImdbId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? ReleaseDate,
    int? Runtime,
    string? Overview,
    string? Tagline,
    string? Status,
    string? Certification,
    List<DiscoverRef> Genres,
    List<DiscoverRef> Studios,
    List<string> Directors,
    double? VoteAverage,
    string? PosterUrl,
    string? BackdropUrl,
    string? TrailerUrl,
    List<DiscoverCastMember> Cast,
    List<DiscoverItem> Recommendations,
    List<DiscoverItem> Similar,
    AvailabilityResponse Availability,
    int? LibraryId,
    RequestSlotsResponse? Requests = null);

/// <param name="InLibraryOnly">A season the library has (TVDB numbering) that TMDB does not list.</param>
public sealed record DiscoverSeasonResponse(int SeasonNumber, string Name, int? EpisodeCount, string? AirDate, string? PosterUrl, bool InLibraryOnly, AvailabilityResponse Availability, RequestSlotsResponse? Requests = null);

/// <param name="LibraryId">The series' id in the library, for the admin only (null for everyone else and when absent).</param>
public sealed record DiscoverSeriesResponse(
    int TmdbId,
    int? TvdbId,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? FirstAirDate,
    string? LastAirDate,
    string? Status,
    int? Runtime,
    string? Overview,
    string? Tagline,
    List<DiscoverRef> Genres,
    List<DiscoverRef> Networks,
    List<string> Creators,
    double? VoteAverage,
    string? PosterUrl,
    string? BackdropUrl,
    string? TrailerUrl,
    List<DiscoverCastMember> Cast,
    List<DiscoverSeasonResponse> Seasons,
    List<DiscoverItem> Recommendations,
    List<DiscoverItem> Similar,
    AvailabilityResponse Availability,
    int? LibraryId,
    RequestSlotsResponse? Requests = null);

public sealed record DiscoverPersonResponse(
    int TmdbId,
    string Name,
    string? Biography,
    string? Birthday,
    string? Deathday,
    string? PlaceOfBirth,
    string? KnownFor,
    string? ProfileUrl,
    List<DiscoverItem> Credits);

// ---- settings (the admin's) ----------------------------------------------------------------------------------------

/// <param name="VersionNames">The version names used in the library, to pick the 4K one from.</param>
public sealed record DiscoverSettingsResponse(string? FourKVersion, List<string> VersionNames, List<DiscoverSliderSettings> Sliders);

public sealed record DiscoverSliderSettings(int Id, DiscoverSliderKind Kind, string? Title, string? Value, bool Enabled, bool BuiltIn);

/// <param name="Sliders">Every row in display order. A custom row left out is deleted; a built-in one cannot be.</param>
public sealed record UpdateDiscoverSettingsRequest(string? FourKVersion, List<UpdateDiscoverSliderRequest> Sliders);

/// <param name="Id">Null for a new custom row.</param>
public sealed record UpdateDiscoverSliderRequest(int? Id, DiscoverSliderKind Kind, string? Title, string? Value, bool Enabled);

public sealed record DiscoverLookupResult(int Id, string Name);
