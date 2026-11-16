using System.Security.Claims;
using Bigarr.Api.Features.Discover;
using Bigarr.Api.Features.Users;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Requests;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Requests;

/// <summary>
/// Requests. Every signed-in user lists their own, asks (as their permissions allow) and cancels a pending one of theirs;
/// admins and request managers see everyone's, approve (with edits), decline and retry. Settings → Requests (the
/// defaults an approval uses) is the admin's.
/// </summary>
public static class RequestsEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapRequests(this IEndpointRouteBuilder app)
    {
        var signedIn = app.MapGroup("/api/v1/requests").WithTags("Requests").RequireAuthorization(AuthPolicies.SignedIn);
        signedIn.MapGet("/", ListAsync);
        signedIn.MapPost("/", CreateAsync);
        signedIn.MapGet("/quota", (ClaimsPrincipal principal, RequestService requests, CancellationToken ct) => requests.QuotaAsync(principal, ct));
        signedIn.MapGet("/{id:int}", GetAsync);
        signedIn.MapDelete("/{id:int}", CancelAsync);

        var managers = app.MapGroup("/api/v1/requests").WithTags("Requests").RequireAuthorization(AuthPolicies.ManageRequests);
        managers.MapGet("/count", CountAsync);
        managers.MapGet("/options", OptionsAsync);
        managers.MapPost("/{id:int}/approve", ApproveAsync);
        managers.MapPost("/{id:int}/decline", DeclineAsync);
        managers.MapPost("/{id:int}/retry", RetryAsync);

        var admin = app.MapGroup("/api/v1/requests").WithTags("Requests").RequireAuthorization(AuthPolicies.Session);
        admin.MapGet("/settings", SettingsAsync);
        admin.MapPut("/settings", UpdateSettingsAsync);
        return app;
    }

    private static RequestMediaType? ParseType(string? value) =>
        Enum.TryParse<RequestMediaType>(value, ignoreCase: true, out var type) && Enum.IsDefined(type) ? type : null;

    private static bool Approver(ClaimsPrincipal principal) =>
        principal.IsAdmin() || principal.IsInRole(nameof(UserRole.RequestManager));

    /// <param name="mediaType"><c>movie</c> or <c>series</c>; both when absent.</param>
    /// <param name="filter">all (default), pending, approved, processing, available (with partially available), declined, failed.</param>
    private static async Task<Ok<MediaRequestPage>> ListAsync(
        ClaimsPrincipal principal, BigarrDbContext db, RequestStates states, RequestTargets targets, CancellationToken ct,
        string? filter = null, string? mediaType = null, int? userId = null, int page = 1, int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = db.MediaRequests.AsNoTracking().Include(r => r.RequestedBy).Include(r => r.DecidedBy).AsQueryable();
        if (!Approver(principal))
        {
            userId = principal.UserId();
        }

        if (userId is not null)
        {
            query = query.Where(r => r.RequestedById == userId);
        }

        if (ParseType(mediaType) is { } type)
        {
            query = query.Where(r => r.MediaType == type);
        }

        var wanted = (filter ?? "all").ToLowerInvariant();
        query = wanted switch
        {
            "pending" => query.Where(r => r.Status == RequestStatus.Pending),
            "declined" => query.Where(r => r.Status == RequestStatus.Declined),
            "failed" => query.Where(r => r.Status == RequestStatus.Failed || (r.Status == RequestStatus.Approved && r.ForwardStatus == ForwardStatus.Failed)),
            "approved" or "processing" or "available" => query.Where(r => r.Status == RequestStatus.Approved),
            _ => query,
        };
        query = query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);

        List<MediaRequest> rows;
        int total;
        Dictionary<int, RequestState> shown;
        if (wanted is "processing" or "available" or "approved")
        {
            // Derived states: read the library for every approved request, then page.
            var all = await query.ToListAsync(ct);
            shown = await states.ResolveAsync(all, ct);
            var keep = all.Where(r => wanted switch
            {
                "processing" => shown[r.Id] == RequestState.Processing,
                "available" => shown[r.Id] is RequestState.Available or RequestState.PartiallyAvailable,
                _ => true,
            }).ToList();
            total = keep.Count;
            rows = [.. keep.Skip((page - 1) * pageSize).Take(pageSize)];
        }
        else
        {
            total = await query.CountAsync(ct);
            rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            shown = await states.ResolveAsync(rows, ct);
        }

        var names = await NamesAsync(db, rows, ct);
        return TypedResults.Ok(new MediaRequestPage(page, pageSize, total, [.. rows.Select(r => ToResponse(r, shown[r.Id], principal, names))]));
    }

    private static async Task<Results<Ok<MediaRequestResponse>, NotFound>> GetAsync(int id, ClaimsPrincipal principal, BigarrDbContext db, RequestStates states, CancellationToken ct)
    {
        var request = await db.MediaRequests.AsNoTracking().Include(r => r.RequestedBy).Include(r => r.DecidedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || (!Approver(principal) && request.RequestedById != principal.UserId()))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await ResponseAsync(db, states, request, principal, ct));
    }

    private static async Task<Results<Created<MediaRequestResponse>, ProblemHttpResult>> CreateAsync(
        CreateRequestRequest body, ClaimsPrincipal principal, RequestService requests, BigarrDbContext db, RequestStates states, CancellationToken ct)
    {
        var result = await requests.CreateAsync(principal, body, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var row = await db.MediaRequests.AsNoTracking().Include(r => r.RequestedBy).Include(r => r.DecidedBy).FirstAsync(r => r.Id == result.Value!.Id, ct);
        return TypedResults.Created($"/api/v1/requests/{row.Id}", await ResponseAsync(db, states, row, principal, ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> CancelAsync(int id, ClaimsPrincipal principal, RequestService requests, CancellationToken ct)
    {
        var result = await requests.CancelAsync(principal, id, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<MediaRequestResponse>, ProblemHttpResult>> ApproveAsync(
        int id, ApproveRequestRequest? body, ClaimsPrincipal principal, RequestService requests, BigarrDbContext db, RequestStates states, CancellationToken ct) =>
        await Decided(await requests.ApproveAsync(principal, id, body ?? new ApproveRequestRequest(), ct), principal, db, states, ct);

    private static async Task<Results<Ok<MediaRequestResponse>, ProblemHttpResult>> DeclineAsync(
        int id, DeclineRequestRequest? body, ClaimsPrincipal principal, RequestService requests, BigarrDbContext db, RequestStates states, CancellationToken ct) =>
        await Decided(await requests.DeclineAsync(principal, id, body?.Reason, ct), principal, db, states, ct);

    private static async Task<Results<Ok<MediaRequestResponse>, ProblemHttpResult>> RetryAsync(
        int id, ClaimsPrincipal principal, RequestService requests, BigarrDbContext db, RequestStates states, CancellationToken ct) =>
        await Decided(await requests.RetryAsync(principal, id, ct), principal, db, states, ct);

    private static async Task<Results<Ok<MediaRequestResponse>, ProblemHttpResult>> Decided(Infrastructure.Result<MediaRequest> result, ClaimsPrincipal principal, BigarrDbContext db, RequestStates states, CancellationToken ct)
    {
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var row = await db.MediaRequests.AsNoTracking().Include(r => r.RequestedBy).Include(r => r.DecidedBy).FirstAsync(r => r.Id == result.Value!.Id, ct);
        return TypedResults.Ok(await ResponseAsync(db, states, row, principal, ct));
    }

    private static async Task<Ok<RequestCountsResponse>> CountAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(new RequestCountsResponse(
            await db.MediaRequests.CountAsync(r => r.Status == RequestStatus.Pending, ct),
            await db.MediaRequests.CountAsync(r => r.Status == RequestStatus.Failed || (r.Status == RequestStatus.Approved && r.ForwardStatus == ForwardStatus.Failed), ct)));

    /// <summary>What an approver may pick from: profiles and root folders of the type, by name (a root folder's folder name; its path for the admin).</summary>
    /// <param name="mediaType"><c>movie</c> or <c>series</c>.</param>
    private static async Task<Results<Ok<RequestOptionsResponse>, ProblemHttpResult>> OptionsAsync(string mediaType, ClaimsPrincipal principal, RequestTargets targets, LibraryAvailability library, CancellationToken ct)
    {
        if (ParseType(mediaType) is not { } type)
        {
            return TypedResults.Problem(title: "Unknown media type", statusCode: StatusCodes.Status400BadRequest);
        }

        var profiles = await targets.ProfilesAsync(type, ct);
        var roots = await targets.RootFoldersAsync(type, ct);
        var standard = await targets.DefaultsAsync(type, fourK: false, ct);
        var fourK = await targets.DefaultsAsync(type, fourK: true, ct);
        return TypedResults.Ok(new RequestOptionsResponse(
            type,
            [.. profiles.Select(p => new RequestOption(p.Id, p.Name))],
            [.. roots.Select(r => new RequestOption(r.Id, principal.IsAdmin() ? r.Path : RequestTargets.FolderName(r.Path)))],
            await library.FourKVersionAsync(ct),
            standard.ProfileId,
            standard.RootFolderId,
            fourK.ProfileId,
            fourK.RootFolderId));
    }

    private static async Task<Ok<RequestDefaultsDto>> SettingsAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(RequestDefaultsDto.From((await UserSettings.ReadAsync(db, ct)).RequestDefaults));

    private static async Task<Ok<RequestDefaultsDto>> UpdateSettingsAsync(RequestDefaultsDto body, BigarrDbContext db, CancellationToken ct)
    {
        var settings = await UserSettings.EditAsync(db, ct);
        settings.RequestDefaults = body.ToDomain();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(RequestDefaultsDto.From(settings.RequestDefaults));
    }

    private sealed record Names(Dictionary<int, string> Profiles, Dictionary<int, string> Roots);

    private static async Task<Names> NamesAsync(BigarrDbContext db, IReadOnlyCollection<MediaRequest> rows, CancellationToken ct)
    {
        var profileIds = rows.Where(r => r.QualityProfileId != null).Select(r => r.QualityProfileId!.Value).Distinct().ToList();
        var rootIds = rows.Where(r => r.RootFolderId != null).Select(r => r.RootFolderId!.Value).Distinct().ToList();
        return new Names(
            await db.QualityProfiles.AsNoTracking().Where(p => profileIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct),
            (await db.RootFolders.AsNoTracking().Where(r => rootIds.Contains(r.Id)).ToListAsync(ct)).ToDictionary(r => r.Id, r => RequestTargets.FolderName(r.Path)));
    }

    private static async Task<MediaRequestResponse> ResponseAsync(BigarrDbContext db, RequestStates states, MediaRequest request, ClaimsPrincipal principal, CancellationToken ct) =>
        ToResponse(request, (await states.ResolveAsync([request], ct))[request.Id], principal, await NamesAsync(db, [request], ct));

    private static MediaRequestResponse ToResponse(MediaRequest r, RequestState state, ClaimsPrincipal principal, Names names)
    {
        var approver = Approver(principal);
        var admin = principal.IsAdmin();
        var mine = r.RequestedById == principal.UserId();
        return new MediaRequestResponse(
            r.Id,
            r.MediaType,
            r.TmdbId,
            r.Title,
            r.Year,
            r.PosterUrl,
            r.FourK,
            r.Seasons,
            state,
            r.Status,
            r.CreatedAt,
            r.UpdatedAt,
            approver ? new RequestUserRef(r.RequestedById, r.RequestedBy.Username) : null,
            approver && r.DecidedBy is { } decider ? new RequestUserRef(decider.Id, decider.Username) : null,
            r.DecidedAt,
            r.AutoApproved,
            r.DeclineReason,
            approver && r.QualityProfileId is { } p ? names.Profiles.GetValueOrDefault(p) : null,
            approver && r.RootFolderId is { } f ? names.Roots.GetValueOrDefault(f) : null,
            approver ? r.ForwardStatus : null,
            approver ? r.ForwardedTo : null,
            admin ? r.ForwardError : null,
            approver ? r.Error : null,
            admin ? r.MovieId ?? r.SeriesId : null,
            mine && r.Status == RequestStatus.Pending);
    }
}
