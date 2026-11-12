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
        group.MapPost("/logout", Logout).RequireAuthorization(AuthPolicies.Session);
        group.MapGet("/me", Me).RequireAuthorization(AuthPolicies.Api);
        group.MapPost("/password", ChangePasswordAsync).RequireAuthorization(AuthPolicies.Session);

        return app;
    }

    // Login only accepts a JSON body: browsers cannot send one cross-site without a CORS preflight,
    // which together with SameSite=Lax on the cookie is the CSRF protection of this endpoint.
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> LoginAsync(
        LoginRequest request,
        HttpContext http,
        BigarrDbContext db,
        TimeProvider time,
        IPasswordHasher<User> hasher,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Username and password are required."],
            });
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username, http.RequestAborted);
        var verdict = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (user is null || verdict == PasswordVerificationResult.Failed)
        {
            loggerFactory.CreateLogger("Bigarr.Auth").LogWarning(
                "Failed login for {Username} from {RemoteIp}", request.Username, http.Connection.RemoteIpAddress);
            return TypedResults.Unauthorized();
        }

        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        user.LastLoginAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(http.RequestAborted);

        await http.SignInAsync(
            AuthSchemes.Session,
            BigarrPrincipal.ForUser(user, AuthSchemes.Session),
            new AuthenticationProperties { IsPersistent = request.RememberMe });

        return TypedResults.NoContent();
    }

    private static SignOutHttpResult Logout() =>
        TypedResults.SignOut(authenticationSchemes: [AuthSchemes.Session]);

    private static Ok<MeResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new MeResponse(
            user.UserId(),
            user.Identity!.Name!,
            user.FindFirstValue(ClaimTypes.Role)!,
            user.Identity.AuthenticationType!,
            user.FindFirstValue(BigarrClaims.CompatView)));

    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        HttpContext http,
        BigarrDbContext db,
        IPasswordHasher<User> hasher)
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
        return TypedResults.NoContent();
    }
}
