using Bigarr.Api.Features.Discover;
using Bigarr.Domain.Requests;

namespace Bigarr.Api.Features.Requests;

// What a request looks like from each side. Everyone gets the title, the slot, the seasons and the state; an approver
// (admin or request manager) also gets who asked, the chosen profile and root folder by name, and the forward state; only
// the admin gets library ids and the forward error text (it can carry a followed app's address).

/// <summary>A request's state as shown: the stored lifecycle, then, once approved, how far the library got.</summary>
public enum RequestState
{
    Pending,
    Approved,
    Declined,
    Failed,

    /// <summary>Approved, monitored in the library, no file yet.</summary>
    Processing,

    /// <summary>Some of the requested episodes have a file (series).</summary>
    PartiallyAvailable,

    Available,
}

/// <param name="Seasons">The seasons to ask for (series); null asks for every season that can still be asked for.</param>
public sealed record CreateRequestRequest(RequestMediaType MediaType, int TmdbId, bool FourK = false, List<int>? Seasons = null);

/// <summary>Approve with edits: every field left null keeps what the request or the defaults say.</summary>
/// <param name="FourK">Moves the request to the other slot (an approver may turn a 4K request into a standard one).</param>
/// <param name="Seasons">The seasons to monitor (series): a subset or a superset of what was asked.</param>
public sealed record ApproveRequestRequest(int? QualityProfileId = null, int? RootFolderId = null, bool? FourK = null, List<int>? Seasons = null);

public sealed record DeclineRequestRequest(string? Reason = null);

public sealed record RequestUserRef(int Id, string Username);

/// <param name="RequestedBy">Null for the user's own list (it is them).</param>
/// <param name="QualityProfile">Approvers only: the profile the approval chose (or will choose), by name.</param>
/// <param name="RootFolder">Approvers only: the root folder's folder name, not its path.</param>
/// <param name="ForwardStatus">Approvers only.</param>
/// <param name="ForwardedTo">Approvers only: the followed Radarr/Sonarr's name.</param>
/// <param name="ForwardError">The admin only.</param>
/// <param name="Error">Approvers only: why the library could not take it.</param>
/// <param name="LibraryId">The admin only: the film or series id in the library.</param>
/// <param name="CanCancel">The viewer may cancel it (their own, still pending).</param>
public sealed record MediaRequestResponse(
    int Id,
    RequestMediaType MediaType,
    int TmdbId,
    string Title,
    int? Year,
    string? PosterUrl,
    bool FourK,
    List<int> Seasons,
    RequestState State,
    RequestStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    RequestUserRef? RequestedBy,
    RequestUserRef? DecidedBy,
    DateTime? DecidedAt,
    bool AutoApproved,
    string? DeclineReason,
    string? QualityProfile,
    string? RootFolder,
    ForwardStatus? ForwardStatus,
    string? ForwardedTo,
    string? ForwardError,
    string? Error,
    int? LibraryId,
    bool CanCancel);

public sealed record MediaRequestPage(int Page, int PageSize, int TotalRecords, List<MediaRequestResponse> Records);

/// <param name="Pending">Requests waiting for a decision (approvers only, else 0).</param>
/// <param name="Failed">Approved requests the library or the forward could not take (approvers only, else 0).</param>
public sealed record RequestCountsResponse(int Pending, int Failed);

/// <param name="Limit">Null: unlimited.</param>
/// <param name="Remaining">Null: unlimited.</param>
public sealed record QuotaUsage(int? Limit, int Days, int Used, int? Remaining);

/// <param name="Exempt">An admin is never limited.</param>
public sealed record QuotaResponse(QuotaUsage Films, QuotaUsage Seasons, bool Exempt);

public sealed record QuotaDto(int? FilmLimit, int FilmDays, int? SeasonLimit, int SeasonDays)
{
    public static QuotaDto From(RequestQuota quota)
    {
        ArgumentNullException.ThrowIfNull(quota);
        return new(quota.FilmLimit, quota.FilmDays, quota.SeasonLimit, quota.SeasonDays);
    }

    public RequestQuota ToDomain() => new()
    {
        FilmLimit = FilmLimit is null ? null : Math.Max(0, FilmLimit.Value),
        FilmDays = Math.Clamp(FilmDays, 1, 365),
        SeasonLimit = SeasonLimit is null ? null : Math.Max(0, SeasonLimit.Value),
        SeasonDays = Math.Clamp(SeasonDays, 1, 365),
    };
}

/// <summary>A profile or a root folder an approver may pick: its id and a name (a root folder's folder name, never its path).</summary>
public sealed record RequestOption(int Id, string Name);

/// <param name="FourKVersion">The 4K version's name; null when only the standard slot is offered.</param>
/// <param name="StandardProfileId">What the approval takes when the approver leaves it, standard slot.</param>
public sealed record RequestOptionsResponse(
    RequestMediaType MediaType,
    List<RequestOption> Profiles,
    List<RequestOption> RootFolders,
    string? FourKVersion,
    int? StandardProfileId,
    int? StandardRootFolderId,
    int? FourKProfileId,
    int? FourKRootFolderId);

public sealed record RequestDefaultsDto(
    int? MovieProfileId,
    int? MovieRootFolderId,
    int? MovieFourKProfileId,
    int? MovieFourKRootFolderId,
    int? SeriesProfileId,
    int? SeriesRootFolderId,
    int? SeriesFourKProfileId,
    int? SeriesFourKRootFolderId)
{
    public static RequestDefaultsDto From(RequestDefaults d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new(d.MovieProfileId, d.MovieRootFolderId, d.MovieFourKProfileId, d.MovieFourKRootFolderId, d.SeriesProfileId, d.SeriesRootFolderId, d.SeriesFourKProfileId, d.SeriesFourKRootFolderId);
    }

    public RequestDefaults ToDomain() => new()
    {
        MovieProfileId = MovieProfileId,
        MovieRootFolderId = MovieRootFolderId,
        MovieFourKProfileId = MovieFourKProfileId,
        MovieFourKRootFolderId = MovieFourKRootFolderId,
        SeriesProfileId = SeriesProfileId,
        SeriesRootFolderId = SeriesRootFolderId,
        SeriesFourKProfileId = SeriesFourKProfileId,
        SeriesFourKRootFolderId = SeriesFourKRootFolderId,
    };
}

/// <summary>A title's (or a season's) active requests per slot, as Discover shows them to everyone.</summary>
public sealed record RequestSlotsResponse(RequestState? Standard, RequestState? FourK);
