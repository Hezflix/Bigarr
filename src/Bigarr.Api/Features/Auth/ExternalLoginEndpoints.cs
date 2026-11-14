using System.Security.Claims;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Auth;

/// <summary>
/// Signing in with an outside account (Plex…), beside the password, never instead of it: the local login always works,
/// even with every provider on and the provider unreachable. A provider gives an identity, not a role: an account signs
/// in only the user it was linked to, from Settings, while signed in with the password. An account linked to no one
/// is refused; no user is created.
/// </summary>
public static class ExternalLoginEndpoints
{
    /// <summary>Holds the flow id in the browser that started the sign-in, so no other browser can finish it.</summary>
    public const string FlowCookie = "bigarr_signin_flow";

    private const string FlowCookiePath = "/api/v1/auth/external";

    public static IEndpointRouteBuilder MapExternalLogin(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Auth");
        auth.MapGet("/providers", ProvidersAsync).AllowAnonymous();

        var group = app.MapGroup(FlowCookiePath).WithTags("Auth");

        // Settings: the human session only, a key must never link an account to its user.
        group.MapGet("/", SettingsAsync).RequireAuthorization(AuthPolicies.Session);
        group.MapPut("/{provider}", UpdateAsync).RequireAuthorization(AuthPolicies.Session);
        group.MapPost("/{provider}/link", StartLinkAsync).RequireAuthorization(AuthPolicies.Session);
        group.MapDelete("/{provider}/link", UnlinkAsync).RequireAuthorization(AuthPolicies.Session);

        // The sign-in itself, from the login page. Poll and cancel take a JSON body: like the password login, a
        // browser cannot send one cross-site without a CORS preflight.
        group.MapPost("/{provider}/login", StartLoginAsync).AllowAnonymous();
        group.MapPost("/poll", PollAsync).AllowAnonymous();
        group.MapPost("/cancel", Cancel).AllowAnonymous();

        return app;
    }

    private static async Task<Ok<List<ExternalProviderResponse>>> ProvidersAsync(
        IEnumerable<IExternalIdentityProvider> providers, BigarrDbContext db, CancellationToken ct)
    {
        var offered = await OfferedAsync(db, ct);
        return TypedResults.Ok(providers
            .Where(p => offered.Contains(p.Key))
            .Select(p => new ExternalProviderResponse(p.Key, p.DisplayName))
            .ToList());
    }

    private static async Task<Ok<List<ExternalLoginSettingsResponse>>> SettingsAsync(
        ClaimsPrincipal principal, IEnumerable<IExternalIdentityProvider> providers, BigarrDbContext db, CancellationToken ct)
    {
        var userId = principal.UserId();
        var enabled = await db.ExternalLoginProviders.AsNoTracking().Where(p => p.Enabled).Select(p => p.Provider).ToListAsync(ct);
        var links = await db.ExternalLogins.AsNoTracking().Where(l => l.UserId == userId).ToListAsync(ct);
        return TypedResults.Ok(providers
            .Select(p => new ExternalLoginSettingsResponse(
                p.Key,
                p.DisplayName,
                enabled.Contains(p.Key),
                links.FirstOrDefault(l => l.Provider == p.Key) is { } link
                    ? new ExternalLinkResponse(link.ExternalId, link.ExternalUsername, link.ThumbUrl, link.LinkedAt, link.LastLoginAt)
                    : null))
            .ToList());
    }

    private static async Task<Results<NoContent, NotFound>> UpdateAsync(
        string provider, UpdateExternalProviderRequest request, IEnumerable<IExternalIdentityProvider> providers, BigarrDbContext db, CancellationToken ct)
    {
        if (Find(providers, provider) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var row = await db.ExternalLoginProviders.FirstOrDefaultAsync(p => p.Provider == found.Key, ct);
        if (row is null)
        {
            db.ExternalLoginProviders.Add(new ExternalLoginProvider { Provider = found.Key, Enabled = request.Enabled });
        }
        else
        {
            row.Enabled = request.Enabled;
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ExternalFlowStartedResponse>, NotFound, ProblemHttpResult>> StartLinkAsync(
        string provider, ClaimsPrincipal principal, HttpContext http, IEnumerable<IExternalIdentityProvider> providers, ExternalSignInFlows flows)
    {
        if (Find(providers, provider) is not { } found)
        {
            return TypedResults.NotFound();
        }

        return await StartAsync(found, principal.UserId(), http, flows);
    }

    private static async Task<Results<NoContent, NotFound>> UnlinkAsync(
        string provider, ClaimsPrincipal principal, IEnumerable<IExternalIdentityProvider> providers, BigarrDbContext db, CancellationToken ct)
    {
        if (Find(providers, provider) is not { } found)
        {
            return TypedResults.NotFound();
        }

        var userId = principal.UserId();
        await db.ExternalLogins.Where(l => l.UserId == userId && l.Provider == found.Key).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ExternalFlowStartedResponse>, NotFound, ProblemHttpResult>> StartLoginAsync(
        string provider, HttpContext http, IEnumerable<IExternalIdentityProvider> providers, BigarrDbContext db, ExternalSignInFlows flows)
    {
        if (Find(providers, provider) is not { } found || !(await OfferedAsync(db, http.RequestAborted)).Contains(found.Key))
        {
            return TypedResults.NotFound();
        }

        return await StartAsync(found, linkUserId: null, http, flows);
    }

    private static async Task<Results<Ok<ExternalFlowStartedResponse>, NotFound, ProblemHttpResult>> StartAsync(
        IExternalIdentityProvider provider, int? linkUserId, HttpContext http, ExternalSignInFlows flows)
    {
        ExternalChallenge challenge;
        try
        {
            challenge = await provider.StartAsync(http.RequestAborted);
        }
        catch (ExternalIdentityException ex)
        {
            return Unreachable(provider, ex);
        }

        var flow = flows.Start(provider.Key, challenge, linkUserId);
        http.Response.Cookies.Append(FlowCookie, flow.Id, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = FlowCookiePath,
            Expires = flow.ExpiresAt,
        });
        return TypedResults.Ok(new ExternalFlowStartedResponse(flow.Id, challenge.AuthUrl, challenge.UserCode, challenge.ExpiresAt));
    }

    private static async Task<Results<Ok<ExternalFlowPollResponse>, ForbidHttpResult, ProblemHttpResult>> PollAsync(
        ExternalFlowRequest request,
        HttpContext http,
        IEnumerable<IExternalIdentityProvider> providers,
        ExternalSignInFlows flows,
        BigarrDbContext db,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        var ct = http.RequestAborted;
        var flow = Owned(request, http, flows);
        if (flow is null || Find(providers, flow.Provider) is not { } provider)
        {
            return TypedResults.Ok(new ExternalFlowPollResponse(ExternalFlowStatus.Expired));
        }

        if (flows.IsExpired(flow))
        {
            return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.Expired));
        }

        if (flow.LinkUserId is { } linkUserId)
        {
            // Linking is finished by the same user, still signed in with the session that started it.
            var session = await http.AuthenticateAsync(AuthSchemes.Session);
            if (!session.Succeeded || session.Principal.UserId() != linkUserId)
            {
                return TypedResults.Forbid(authenticationSchemes: [AuthSchemes.Session]);
            }
        }
        else if (!(await OfferedAsync(db, ct)).Contains(provider.Key))
        {
            return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.Disabled));
        }

        ExternalPoll poll;
        try
        {
            poll = await provider.PollAsync(flow.Challenge, ct);
        }
        catch (ExternalIdentityException ex)
        {
            // The flow stays: plex.tv may answer the next poll.
            return Unreachable(provider, ex);
        }

        switch (poll.Status)
        {
            case ExternalPollStatus.Pending:
                return TypedResults.Ok(new ExternalFlowPollResponse(ExternalFlowStatus.Pending));
            case ExternalPollStatus.Expired:
                return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.Expired));
            case ExternalPollStatus.Denied or ExternalPollStatus.Completed when poll.Identity is null:
                return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.Denied));
        }

        var identity = poll.Identity!;
        var logger = loggerFactory.CreateLogger("Bigarr.Auth");
        var now = time.GetUtcNow().UtcDateTime;
        var link = await db.ExternalLogins.Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Provider == provider.Key && l.ExternalId == identity.ExternalId, ct);

        if (flow.LinkUserId is { } userId)
        {
            if (link is not null && link.UserId != userId)
            {
                return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.LinkedElsewhere, identity.Username));
            }

            link ??= await db.ExternalLogins.FirstOrDefaultAsync(l => l.UserId == userId && l.Provider == provider.Key, ct);
            if (link is null)
            {
                link = new ExternalLogin { Provider = provider.Key, ExternalId = identity.ExternalId, UserId = userId };
                db.ExternalLogins.Add(link);
            }
            else if (link.ExternalId != identity.ExternalId)
            {
                // Another account of the same provider replaces the one linked before.
                link.ExternalId = identity.ExternalId;
                link.LinkedAt = now;
                link.LastLoginAt = null;
            }

            Refresh(link, identity);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("{Provider} account {Account} linked to user {UserId}", provider.DisplayName, identity.Username, userId);
            return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.Linked, identity.Username));
        }

        if (link is null)
        {
            logger.LogWarning(
                "Sign-in with {Provider} refused: account {Account} is linked to no user (from {RemoteIp})",
                provider.DisplayName, identity.Username, http.Connection.RemoteIpAddress);
            return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.UnknownAccount, identity.Username));
        }

        Refresh(link, identity);
        link.LastLoginAt = now;
        link.User.LastLoginAt = now;
        await db.SaveChangesAsync(ct);

        await http.SignInAsync(
            AuthSchemes.Session,
            BigarrPrincipal.ForUser(link.User, AuthSchemes.Session),
            new AuthenticationProperties { IsPersistent = true });
        return Ended(http, flows, flow, new ExternalFlowPollResponse(ExternalFlowStatus.SignedIn, identity.Username));
    }

    private static NoContent Cancel(ExternalFlowRequest request, HttpContext http, ExternalSignInFlows flows)
    {
        if (Owned(request, http, flows) is { } flow)
        {
            flows.End(flow.Id);
        }

        http.Response.Cookies.Delete(FlowCookie, new CookieOptions { Path = FlowCookiePath });
        return TypedResults.NoContent();
    }

    /// <summary>The flow, if this browser started it.</summary>
    private static ExternalSignInFlow? Owned(ExternalFlowRequest request, HttpContext http, ExternalSignInFlows flows) =>
        !string.IsNullOrEmpty(request.FlowId)
        && http.Request.Cookies.TryGetValue(FlowCookie, out var cookie)
        && string.Equals(cookie, request.FlowId, StringComparison.Ordinal)
            ? flows.Find(request.FlowId)
            : null;

    private static Ok<ExternalFlowPollResponse> Ended(HttpContext http, ExternalSignInFlows flows, ExternalSignInFlow flow, ExternalFlowPollResponse response)
    {
        flows.End(flow.Id);
        http.Response.Cookies.Delete(FlowCookie, new CookieOptions { Path = FlowCookiePath });
        return TypedResults.Ok(response);
    }

    private static void Refresh(ExternalLogin link, ExternalIdentity identity)
    {
        link.ExternalUsername = Truncate(identity.Username, 256);
        link.ThumbUrl = identity.ThumbUrl is { Length: <= 1024 } thumb && Uri.TryCreate(thumb, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? thumb : null;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private static ProblemHttpResult Unreachable(IExternalIdentityProvider provider, ExternalIdentityException ex) =>
        TypedResults.Problem(
            title: $"{provider.DisplayName} could not be reached.",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);

    private static IExternalIdentityProvider? Find(IEnumerable<IExternalIdentityProvider> providers, string key) =>
        providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Providers the login page offers: turned on, and linked to at least one user.</summary>
    private static async Task<HashSet<string>> OfferedAsync(BigarrDbContext db, CancellationToken ct)
    {
        var offered = await db.ExternalLoginProviders.AsNoTracking()
            .Where(p => p.Enabled && db.ExternalLogins.Any(l => l.Provider == p.Provider))
            .Select(p => p.Provider)
            .ToListAsync(ct);
        return offered.ToHashSet(StringComparer.Ordinal);
    }
}
