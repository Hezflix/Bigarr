using System.Security.Claims;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Auth;

public static class AuthEndpoints
{
    public const int MinPasswordLength = 8;

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", Logout).RequireAuthorization(AuthPolicies.SignedIn);
        group.MapGet("/me", Me).RequireAuthorization(AuthPolicies.SignedIn);
        group.MapPost("/password", ChangePasswordAsync).RequireAuthorization(AuthPolicies.Session);

        return app;
    }

    // Login only accepts a JSON body: browsers cannot send one cross-site without a CORS preflight,
    // which together with SameSite=Lax on the cookie is the CSRF protection of this endpoint.
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext http,
        BigarrDbContext db,
        TimeProvider time,
        IPasswordHasher<User> hasher,
        SessionStamps stamps,
        SignInThrottle throttle,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Username and password are required."],
            });
        }

        // Past the limit the answer comes before the database read and the hash, and it is the same whether the
        // account exists or not.
        var address = http.Connection.RemoteIpAddress;
        if (throttle.RetryAfter(request.Username, address) is { } wait)
        {
            loggerFactory.CreateLogger("Bigarr.Auth").LogWarning(
                "Sign-in refused for {Username} from {RemoteIp}: too many failed attempts", request.Username, address);
            return TooManyAttempts(http, wait);
        }

        // A user without a password (created by a Plex sign-in) never signs in here; a disabled one nowhere. Both get
        // the same answer as a wrong password, so the login form tells nothing about who exists.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username, http.RequestAborted);
        var verdict = user is null || !user.HasPassword
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (user is null || verdict == PasswordVerificationResult.Failed || user.IsDisabled)
        {
            loggerFactory.CreateLogger("Bigarr.Auth").LogWarning(
                user?.IsDisabled == true && verdict != PasswordVerificationResult.Failed
                    ? "Login refused for {Username} from {RemoteIp}: the user is disabled"
                    : "Failed login for {Username} from {RemoteIp}",
                request.Username,
                address);
            throttle.Failed(request.Username, address);
            return TypedResults.Unauthorized();
        }

        throttle.Succeeded(request.Username);

        var rehashed = verdict == PasswordVerificationResult.SuccessRehashNeeded;
        if (rehashed)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        user.LastLoginAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(http.RequestAborted);
        if (rehashed)
        {
            stamps.Changed(user);
        }

        await http.SignInAsync(
            AuthSchemes.Session,
            BigarrPrincipal.ForUser(user, AuthSchemes.Session),
            new AuthenticationProperties { IsPersistent = request.RememberMe });

        return TypedResults.NoContent();
    }

    /// <summary>The answer of a sign-in route that is past its limit, with the wait in whole seconds (at least 1).</summary>
    internal static ProblemHttpResult TooManyAttempts(HttpContext http, TimeSpan wait)
    {
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many failed sign-ins",
            detail: "Wait and try again.");
    }

    private static SignOutHttpResult Logout() =>
        TypedResults.SignOut(authenticationSchemes: [AuthSchemes.Session]);

    private static Ok<MeResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new MeResponse(
            user.UserId(),
            user.Identity!.Name!,
            user.FindFirstValue(ClaimTypes.Role)!,
            user.Identity.AuthenticationType!,
            user.FindFirstValue(BigarrClaims.CompatView),
            [.. user.Permissions()]));

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        BigarrDbContext db,
        IPasswordHasher<User> hasher,
        SessionStamps stamps)
    {
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newPassword"] = [$"The new password must be at least {MinPasswordLength} characters."],
            });
        }

        var user = await db.Users.FirstAsync(u => u.Id == principal.UserId(), http.RequestAborted);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return TypedResults.Unauthorized();
        }

        user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
        await db.SaveChangesAsync(http.RequestAborted);

        // Every session opened with the old password ends here; this one goes on with a cookie for the new one.
        stamps.Changed(user);
        var current = await http.AuthenticateAsync(AuthSchemes.Session);
        await http.SignInAsync(
            AuthSchemes.Session,
            BigarrPrincipal.ForUser(user, AuthSchemes.Session),
            new AuthenticationProperties { IsPersistent = current.Properties?.IsPersistent ?? true });
        return TypedResults.NoContent();
    }
}
