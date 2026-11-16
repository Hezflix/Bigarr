using Bigarr.Domain.Requests;

namespace Bigarr.Domain.Events;

/// <summary>The request at the moment of the event: what a request notification (R5) needs, without asking Bigarr back.</summary>
/// <param name="TmdbId">Null for a series only TVDB has.</param>
/// <param name="Seasons">The seasons asked for (series); empty for a film.</param>
/// <param name="RequestedBy">The username of who asked.</param>
/// <param name="RequestedByUserId">Their id, to find their Discord id and opt-ins.</param>
public sealed record RequestRef(
    int Id,
    RequestMediaType MediaType,
    int? TmdbId,
    int? TvdbId,
    string Title,
    int? Year,
    string? PosterUrl,
    bool FourK,
    List<int> Seasons,
    string RequestedBy,
    int RequestedByUserId)
{
    public static RequestRef From(MediaRequest request, string requestedBy)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RequestRef(request.Id, request.MediaType, request.TmdbId, request.TvdbId, request.Title, request.Year, request.PosterUrl, request.FourK, [.. request.Seasons], requestedBy, request.RequestedById);
    }
}

/// <summary>A user asked for a film or seasons; <paramref name="AutoApproved"/> when their flags approve it at once.</summary>
public sealed record RequestCreated(RequestRef Request, bool AutoApproved) : DomainEvent;

/// <param name="ApprovedBy">Null when the user's flags approved it.</param>
public sealed record RequestApproved(RequestRef Request, string? ApprovedBy, bool AutoApproved) : DomainEvent;

public sealed record RequestDeclined(RequestRef Request, string DeclinedBy, string? Reason) : DomainEvent;

/// <summary>The library has every file of what was asked.</summary>
public sealed record RequestAvailable(RequestRef Request) : DomainEvent;

/// <param name="Reason">Why the library could not take it, or why the followed Radarr/Sonarr refused it.</param>
/// <param name="Forward">The failure is the forward to the followed Radarr/Sonarr, not the library.</param>
public sealed record RequestFailed(RequestRef Request, string Reason, bool Forward) : DomainEvent;
