using System.Security.Claims;
using Bigarr.Api.Features.Discover;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// The request lifecycle (STATE-MACHINES.md, "Request"): created pending, or approved at once by the user's flags; approved
/// (with edits) or declined by an admin or a request manager; cancelled by its author while pending; retried when the
/// library or the forward failed. Every step goes to the outbox for the notifications to come.
/// </summary>
public sealed class RequestService(
    BigarrDbContext db,
    DiscoverService discover,
    LibraryAvailability library,
    RequestTargets targets,
    RequestLibrary libraryAdd,
    OutboxWriter outbox,
    TimeProvider time,
    ILogger<RequestService> logger)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<Result<MediaRequest>> CreateAsync(ClaimsPrincipal principal, CreateRequestRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!Enum.IsDefined(body.MediaType))
        {
            return Failure.Invalid("Unknown media type");
        }

        var permission = body.MediaType == RequestMediaType.Movie ? UserPermission.RequestFilms : UserPermission.RequestSeries;
        if (!principal.Has(permission) || (body.FourK && !principal.Has(UserPermission.Request4K)))
        {
            return new Failure(StatusCodes.Status403Forbidden, "Not allowed", body.FourK ? "You may not request this in 4K." : "You may not request this type of title.");
        }

        if (body.FourK && await library.FourKVersionAsync(ct) is null)
        {
            return Failure.Invalid("4K is not offered");
        }

        if (!discover.Configured)
        {
            return new Failure(StatusCodes.Status503ServiceUnavailable, "Requests need a TMDB API key.");
        }

        var request = new MediaRequest
        {
            MediaType = body.MediaType,
            TmdbId = body.TmdbId,
            FourK = body.FourK,
            Title = string.Empty,
            RequestedById = principal.UserId(),
            UpdatedAt = Now,
        };

        // The title as TMDB has it, and what the library and the other requests already hold of it.
        var taken = await ActiveAsync(body.MediaType, body.TmdbId, body.FourK, ct);
        if (body.MediaType == RequestMediaType.Movie)
        {
            var movie = await discover.MovieAsync(body.TmdbId, admin: false, ct);
            if (movie is null)
            {
                return Failure.NotFound($"TMDB film {body.TmdbId}");
            }

            (request.Title, request.Year, request.PosterUrl) = (movie.Title, movie.Year, movie.PosterUrl);
            if (Slot(movie.Availability, body.FourK) != AvailabilityState.NotInLibrary)
            {
                return Failure.Conflict("Already in the library", $"{movie.Title} is already wanted or available in this version.");
            }

            if (taken.Count > 0)
            {
                return Failure.Conflict("Already requested", $"{movie.Title} is already requested in this version.");
            }
        }
        else
        {
            var show = await discover.SeriesAsync(body.TmdbId, admin: false, ct);
            if (show is null)
            {
                return Failure.NotFound($"TMDB series {body.TmdbId}");
            }

            (request.Title, request.Year, request.PosterUrl, request.TvdbId) = (show.Title, show.Year, show.PosterUrl, show.TvdbId);
            var known = show.Seasons.Select(s => s.SeasonNumber).ToHashSet();
            var unknown = (body.Seasons ?? []).Where(n => !known.Contains(n)).ToList();
            if (unknown.Count > 0)
            {
                return Failure.Invalid("Unknown season", $"{show.Title} has no season {string.Join(", ", unknown)}.");
            }

            var requested = taken.SelectMany(r => r.Seasons).ToHashSet();
            var open = show.Seasons
                .Where(s => Slot(s.Availability, body.FourK) == AvailabilityState.NotInLibrary && !requested.Contains(s.SeasonNumber))
                .Select(s => s.SeasonNumber)
                .ToHashSet();

            // Without a list: every regular season still open. A season already there or already asked for is left out.
            var asked = body.Seasons ?? [.. show.Seasons.Select(s => s.SeasonNumber).Where(n => n > 0)];
            request.Seasons = [.. asked.Where(open.Contains).Distinct().Order()];
            if (request.Seasons.Count == 0)
            {
                return Failure.Conflict("Nothing left to request", $"Every season of {show.Title} asked for is already requested or in the library in this version.");
            }

            request.AllSeasons = open.Where(n => n > 0).All(request.Seasons.Contains);
        }

        if (!principal.IsAdmin() && await QuotaFailureAsync(request, ct) is { } quota)
        {
            return quota;
        }

        db.MediaRequests.Add(request);
        await db.SaveChangesAsync(ct);
        var username = principal.Identity?.Name ?? string.Empty;
        var auto = principal.Has(body.MediaType == RequestMediaType.Movie ? UserPermission.AutoApproveFilms : UserPermission.AutoApproveSeries);
        outbox.Publish(new RequestCreated(RequestRef.From(request, username), auto));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("{User} requested {Title} ({Type}{FourK}{Seasons})", username, request.Title, request.MediaType, request.FourK ? ", 4K" : string.Empty, request.Seasons.Count > 0 ? ", seasons " + string.Join(',', request.Seasons) : string.Empty);

        if (auto)
        {
            request.AutoApproved = true;
            await ApproveCoreAsync(request, null, null, null, username, ct);
        }

        return Result<MediaRequest>.Ok(request);
    }

    public async Task<Result<MediaRequest>> ApproveAsync(ClaimsPrincipal principal, int id, ApproveRequestRequest edits, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var request = await db.MediaRequests.Include(r => r.RequestedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Failure.NotFound("Request");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return Failure.Conflict("Not pending", $"The request is {request.Status.ToString().ToLowerInvariant()}.");
        }

        if (edits.FourK is { } fourK && fourK != request.FourK)
        {
            if (fourK && await library.FourKVersionAsync(ct) is null)
            {
                return Failure.Invalid("4K is not offered");
            }

            request.FourK = fourK;
        }

        if (edits.Seasons is { } seasons && request.MediaType == RequestMediaType.Series)
        {
            var chosen = seasons.Where(n => n >= 0).Distinct().Order().ToList();
            if (chosen.Count == 0)
            {
                return Failure.Invalid("No season", "Approve at least one season, or decline the request.");
            }

            request.AllSeasons = request.AllSeasons && request.Seasons.All(chosen.Contains);
            request.Seasons = chosen;
        }

        request.DecidedById = principal.UserId();
        request.DecidedAt = Now;
        await ApproveCoreAsync(request, edits.QualityProfileId, edits.RootFolderId, principal.Identity?.Name, request.RequestedBy.Username, ct);
        return Result<MediaRequest>.Ok(request);
    }

    public async Task<Result<MediaRequest>> DeclineAsync(ClaimsPrincipal principal, int id, string? reason, CancellationToken ct)
    {
        var request = await db.MediaRequests.Include(r => r.RequestedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Failure.NotFound("Request");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return Failure.Conflict("Not pending", $"The request is {request.Status.ToString().ToLowerInvariant()}.");
        }

        request.Status = RequestStatus.Declined;
        request.DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 1024)];
        request.DecidedById = principal.UserId();
        request.DecidedAt = Now;
        request.UpdatedAt = Now;
        outbox.Publish(new RequestDeclined(RequestRef.From(request, request.RequestedBy.Username), principal.Identity?.Name ?? string.Empty, request.DeclineReason));
        await db.SaveChangesAsync(ct);
        return Result<MediaRequest>.Ok(request);
    }

    /// <summary>The author takes back a request nobody decided on yet: it is gone, and no longer counts against their quota.</summary>
    public async Task<Result<bool>> CancelAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var request = await db.MediaRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || (request.RequestedById != principal.UserId() && !principal.IsInRole(nameof(UserRole.Admin)) && !principal.IsInRole(nameof(UserRole.RequestManager))))
        {
            return Failure.NotFound("Request");
        }

        if (request.RequestedById != principal.UserId() || request.Status != RequestStatus.Pending)
        {
            return Failure.Conflict("Cannot cancel", "Only its author cancels a request, while it is pending. An approver declines it.");
        }

        db.MediaRequests.Remove(request);
        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    /// <summary>Again what failed: the library add (status failed), else the forward.</summary>
    public async Task<Result<MediaRequest>> RetryAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var request = await db.MediaRequests.Include(r => r.RequestedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return Failure.NotFound("Request");
        }

        if (request.Status == RequestStatus.Failed)
        {
            await ApproveCoreAsync(request, request.QualityProfileId, request.RootFolderId, principal.Identity?.Name, request.RequestedBy.Username, ct, retry: true);
            return Result<MediaRequest>.Ok(request);
        }

        if (request.Status == RequestStatus.Approved && request.ForwardStatus == ForwardStatus.Failed)
        {
            await ForwardAsync(request, request.RequestedBy.Username, ct);
            return Result<MediaRequest>.Ok(request);
        }

        return Failure.Conflict("Nothing to retry", "Only a failed request, or a failed forward, is retried.");
    }

    /// <summary>
    /// Approved: into the library (failed when it cannot take it), then to the followed Radarr/Sonarr of its version. The
    /// approval is saved first, so a crash in between leaves an approved request, never a lost one.
    /// </summary>
    private async Task ApproveCoreAsync(MediaRequest request, int? profileId, int? rootFolderId, string? approver, string requestedBy, CancellationToken ct, bool retry = false)
    {
        request.Status = RequestStatus.Approved;
        request.Error = null;
        request.UpdatedAt = Now;
        if (!retry)
        {
            outbox.Publish(new RequestApproved(RequestRef.From(request, requestedBy), request.AutoApproved ? null : approver, request.AutoApproved));
        }

        await db.SaveChangesAsync(ct);

        var resolved = await targets.ResolveAsync(request.MediaType, request.FourK, profileId, rootFolderId, ct);
        Result<bool> added;
        if (resolved.IsSuccess)
        {
            var (profile, root) = resolved.Value;
            request.QualityProfileId = profile.Id;
            request.RootFolderId = root.Id;
            try
            {
                added = await libraryAdd.AddAsync(request, profile, root, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or DbUpdateException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Request {Id} ({Title}) could not be added to the library", request.Id, request.Title);
                added = new Failure(StatusCodes.Status500InternalServerError, "The library could not take it", ex.Message);
            }
        }
        else
        {
            added = resolved.Failure!;
        }

        // The library's add may have left tracked entities in an odd state on failure: start clean for the request.
        db.ChangeTracker.Clear();
        db.Attach(request);
        request.UpdatedAt = Now;
        if (!added.IsSuccess)
        {
            request.Status = RequestStatus.Failed;
            request.Error = string.IsNullOrEmpty(added.Failure!.Detail) ? added.Failure.Title : $"{added.Failure.Title}: {added.Failure.Detail}";
            outbox.Publish(new RequestFailed(RequestRef.From(request, requestedBy), request.Error, Forward: false));
            db.Entry(request).State = EntityState.Modified;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Request {Id} ({Title}) failed: {Error}", request.Id, request.Title, request.Error);
            return;
        }

        db.Entry(request).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        await ForwardAsync(request, requestedBy, ct);
    }

    private async Task ForwardAsync(MediaRequest request, string requestedBy, CancellationToken ct)
    {
        await libraryAdd.ForwardAsync(request, ct);
        request.UpdatedAt = Now;
        if (request.ForwardStatus == ForwardStatus.Failed)
        {
            outbox.Publish(new RequestFailed(RequestRef.From(request, requestedBy), request.ForwardError ?? "Forward failed", Forward: true));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The active requests for a title and slot (pending, approved, failed): the duplicate check and the season picker.</summary>
    public async Task<List<MediaRequest>> ActiveAsync(RequestMediaType type, int tmdbId, bool fourK, CancellationToken ct) =>
        await db.MediaRequests.AsNoTracking()
            .Where(r => r.MediaType == type && r.TmdbId == tmdbId && r.FourK == fourK && r.Status != RequestStatus.Declined)
            .ToListAsync(ct);

    public async Task<QuotaResponse> QuotaAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == principal.UserId(), ct);
        var (films, seasons) = await UsedAsync(user, ct);
        var exempt = user.Role == UserRole.Admin;
        QuotaUsage Usage(int? limit, int days, int used) => exempt
            ? new QuotaUsage(null, days, used, null)
            : new QuotaUsage(limit, days, used, limit is null ? null : Math.Max(0, limit.Value - used));
        return new QuotaResponse(Usage(user.Quota.FilmLimit, user.Quota.FilmDays, films), Usage(user.Quota.SeasonLimit, user.Quota.SeasonDays, seasons), exempt);
    }

    /// <summary>Films and seasons asked for over each window; a declined request does not count, a cancelled one is gone.</summary>
    private async Task<(int Films, int Seasons)> UsedAsync(User user, CancellationToken ct)
    {
        var since = Now.AddDays(-Math.Max(user.Quota.FilmDays, user.Quota.SeasonDays));
        var mine = await db.MediaRequests.AsNoTracking()
            .Where(r => r.RequestedById == user.Id && r.Status != RequestStatus.Declined && r.CreatedAt >= since)
            .Select(r => new { r.MediaType, r.CreatedAt, r.Seasons })
            .ToListAsync(ct);
        var films = mine.Count(r => r.MediaType == RequestMediaType.Movie && r.CreatedAt >= Now.AddDays(-user.Quota.FilmDays));
        var seasons = mine.Where(r => r.MediaType == RequestMediaType.Series && r.CreatedAt >= Now.AddDays(-user.Quota.SeasonDays)).Sum(r => r.Seasons.Count);
        return (films, seasons);
    }

    private async Task<Failure?> QuotaFailureAsync(MediaRequest request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == request.RequestedById, ct);
        var (films, seasons) = await UsedAsync(user, ct);
        if (request.MediaType == RequestMediaType.Movie && user.Quota.FilmLimit is { } filmLimit && films + 1 > filmLimit)
        {
            return new Failure(StatusCodes.Status403Forbidden, "Request limit reached", $"You may request {filmLimit} film(s) every {user.Quota.FilmDays} day(s).");
        }

        if (request.MediaType == RequestMediaType.Series && user.Quota.SeasonLimit is { } seasonLimit && seasons + request.Seasons.Count > seasonLimit)
        {
            return new Failure(StatusCodes.Status403Forbidden, "Request limit reached", $"You may request {seasonLimit} season(s) every {user.Quota.SeasonDays} day(s); {Math.Max(0, seasonLimit - seasons)} left.");
        }

        return null;
    }

    private static AvailabilityState Slot(AvailabilityResponse availability, bool fourK) =>
        fourK ? availability.FourK ?? AvailabilityState.NotInLibrary : availability.Standard;
}
