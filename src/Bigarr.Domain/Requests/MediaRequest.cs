namespace Bigarr.Domain.Requests;

public enum RequestMediaType
{
    Movie = 1,
    Series = 2,
}

/// <summary>
/// What is stored of a request's lifecycle. Processing, partially available and available are never stored: they are
/// read from the library each time (see STATE-MACHINES.md, "Request").
/// </summary>
public enum RequestStatus
{
    /// <summary>Waiting for an admin or a request manager.</summary>
    Pending = 1,

    /// <summary>Approved and in the library (or forwarded): from here the library says how far it got.</summary>
    Approved = 2,

    /// <summary>Turned down, with an optional reason. Final: asking again makes a new request.</summary>
    Declined = 3,

    /// <summary>Approved but the library could not take it (metadata provider down, title unknown…); retried by hand.</summary>
    Failed = 4,
}

/// <summary>Whether the approved request also went to the Radarr or Sonarr Bigarr follows for its version (forward mode).</summary>
public enum ForwardStatus
{
    /// <summary>No followed Radarr/Sonarr for the version, or not approved yet: nothing to forward.</summary>
    None = 0,

    /// <summary>Added there through its API, monitored, searched on add.</summary>
    Forwarded = 1,

    /// <summary>The title was already there: treated as forwarded (and, for a series, the requested seasons monitored there).</summary>
    AlreadyThere = 2,

    /// <summary>The call failed; the request is kept and the forward can be retried.</summary>
    Failed = 3,
}

/// <summary>
/// A user's request for a film, or for seasons of a series, in one version slot: standard (the title's default version)
/// or 4K (the version named in Discover settings). Approved, it is added to the library through the library's own add
/// path, and to the followed Radarr/Sonarr of that version while one exists.
/// </summary>
public sealed class MediaRequest
{
    public int Id { get; set; }

    public RequestMediaType MediaType { get; set; }

    /// <summary>Null only for a series TMDB does not have, added by an admin from its TVDB id.</summary>
    public int? TmdbId { get; set; }

    /// <summary>A series' TVDB id when known: the library and Sonarr key series by it.</summary>
    public int? TvdbId { get; set; }

    public required string Title { get; set; }

    public int? Year { get; set; }

    public string? PosterUrl { get; set; }

    public bool FourK { get; set; }

    /// <summary>The seasons asked for (series only), ascending; empty for a film.</summary>
    public List<int> Seasons { get; set; } = [];

    /// <summary>A series request that asked for every season it could: new seasons are monitored as they come.</summary>
    public bool AllSeasons { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public int RequestedById { get; set; }

    public Auth.User RequestedBy { get; set; } = null!;

    [SetWhenAdded]
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Who approved or declined it; null while pending and when the user's flags approved it at once.</summary>
    public int? DecidedById { get; set; }

    public Auth.User? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>Approved as soon as it was made, by the user's auto-approve flag (or because an admin asked).</summary>
    public bool AutoApproved { get; set; }

    public string? DeclineReason { get; set; }

    /// <summary>Why the library could not take it (status <see cref="RequestStatus.Failed"/>).</summary>
    public string? Error { get; set; }

    /// <summary>
    /// An admin's add from Discover: the library add's own options (version name, monitoring, search on add, series
    /// type…). Null for a request, which takes the request defaults.
    /// </summary>
    public AdminAddOptions? AddOptions { get; set; }

    // ---- what the approval chose (approve with edits) ----

    public int? QualityProfileId { get; set; }

    public int? RootFolderId { get; set; }

    /// <summary>The library version the request landed in.</summary>
    public string? VersionName { get; set; }

    public int? MovieId { get; set; }

    public int? SeriesId { get; set; }

    // ---- forward mode ----

    public ForwardStatus ForwardStatus { get; set; }

    /// <summary>The followed Radarr/Sonarr it went to (or failed to reach), by name.</summary>
    public string? ForwardedTo { get; set; }

    public string? ForwardError { get; set; }

    public DateTime? ForwardedAt { get; set; }

    /// <summary>When the library first had all of it; the "available" event goes out once.</summary>
    public DateTime? AvailableAt { get; set; }

    /// <summary>A request that still holds its slot: a new one for the same title, version (and season) is refused.</summary>
    public bool IsActive => Status is RequestStatus.Pending or RequestStatus.Approved or RequestStatus.Failed;
}

/// <summary>
/// What the admin's add form sets beside the profile and the root folder, everything the library add offers. Kept on the
/// request so a retry adds the title the same way.
/// </summary>
public sealed record AdminAddOptions
{
    /// <summary>The version's name; null takes the slot's (the standard name, or the 4K one).</summary>
    public string? VersionName { get; init; }

    public bool Monitored { get; init; } = true;

    public bool SearchOnAdd { get; init; } = true;

    /// <summary>Films.</summary>
    public Movies.MinimumAvailability MinimumAvailability { get; init; } = Movies.MinimumAvailability.Released;

    /// <summary>Series: null takes the metadata's suggestion.</summary>
    public Tv.SeriesType? SeriesType { get; init; }

    /// <summary>Series: a monitoring rule (all, future, missing…); null monitors the request's seasons.</summary>
    public Tv.MonitoringOption? Monitor { get; init; }

    public bool SeasonFolder { get; init; } = true;

    /// <summary>Series: null follows the request (new seasons monitored only when every season was asked for).</summary>
    public Tv.NewItemMonitoring? MonitorNewItems { get; init; }
}

/// <summary>
/// How much a user may ask for over a sliding window: films and seasons apart. A null limit is unlimited. Kept on the
/// user and, as the default for new users, on the instance settings.
/// </summary>
public sealed class RequestQuota
{
    public int? FilmLimit { get; set; }

    public int FilmDays { get; set; } = 7;

    public int? SeasonLimit { get; set; }

    public int SeasonDays { get; set; } = 7;

    public RequestQuota Copy() => new() { FilmLimit = FilmLimit, FilmDays = FilmDays, SeasonLimit = SeasonLimit, SeasonDays = SeasonDays };
}

/// <summary>
/// The quality profile and root folder an approved request uses when the approver does not pick one, per type and version
/// slot. Null: the first profile and root folder of the right kind (a 4K-looking profile for the 4K slot).
/// </summary>
public sealed class RequestDefaults
{
    public int? MovieProfileId { get; set; }

    public int? MovieRootFolderId { get; set; }

    public int? MovieFourKProfileId { get; set; }

    public int? MovieFourKRootFolderId { get; set; }

    public int? SeriesProfileId { get; set; }

    public int? SeriesRootFolderId { get; set; }

    public int? SeriesFourKProfileId { get; set; }

    public int? SeriesFourKRootFolderId { get; set; }
}
