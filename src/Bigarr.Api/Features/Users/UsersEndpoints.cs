using System.Security.Claims;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.MediaServers;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Users;

/// <summary>
/// Settings → Users: who can sign in, with which role and permissions, and what a new user starts with. The admin's
/// session only. No delete: a user is disabled, which ends their sessions and API keys and keeps what they did.
/// </summary>
public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization(AuthPolicies.Session);

        group.MapGet("/", ListAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapPost("/{id:int}/disable", (int id, ClaimsPrincipal principal, BigarrDbContext db, SessionStamps stamps, TimeProvider time, ILoggerFactory logs, CancellationToken ct) =>
            SetDisabledAsync(id, disabled: true, principal, db, stamps, time, logs, ct));
        group.MapPost("/{id:int}/enable", (int id, ClaimsPrincipal principal, BigarrDbContext db, SessionStamps stamps, TimeProvider time, ILoggerFactory logs, CancellationToken ct) =>
            SetDisabledAsync(id, disabled: false, principal, db, stamps, time, logs, ct));
        group.MapGet("/settings", SettingsAsync);
        group.MapPut("/settings", UpdateSettingsAsync);

        return app;
    }

    private static async Task<Ok<List<UserResponse>>> ListAsync(ClaimsPrincipal principal, BigarrDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Username).ToListAsync(ct);
        var logins = (await db.ExternalLogins.AsNoTracking().ToListAsync(ct)).ToLookup(l => l.UserId);
        return TypedResults.Ok(users.Select(u => ToResponse(u, logins[u.Id], me)).ToList());
    }

    private static async Task<Results<Ok<UserResponse>, NotFound, ProblemHttpResult>> UpdateAsync(
        int id, UpdateUserRequest request, ClaimsPrincipal principal, BigarrDbContext db, SessionStamps stamps, ILoggerFactory logs, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Role))
        {
            return TypedResults.Problem(title: "Unknown role.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        // Taking the admin role from yourself could leave Bigarr with no admin: another admin does it.
        if (user.Id == principal.UserId() && request.Role != user.Role)
        {
            return TypedResults.Problem(title: "You cannot change your own role.", statusCode: StatusCodes.Status409Conflict);
        }

        var before = user.Role;
        user.Role = request.Role;
        user.Permissions = request.Permissions.ToDomain();
        await db.SaveChangesAsync(ct);
        stamps.Changed(user);
        if (before != user.Role)
        {
            logs.CreateLogger("Bigarr.Auth").LogInformation("User {Username} is now {Role} (was {Before})", user.Username, user.Role, before);
        }

        var logins = await db.ExternalLogins.AsNoTracking().Where(l => l.UserId == id).ToListAsync(ct);
        return TypedResults.Ok(ToResponse(user, logins, principal.UserId()));
    }

    private static async Task<Results<Ok<UserResponse>, NotFound, ProblemHttpResult>> SetDisabledAsync(
        int id, bool disabled, ClaimsPrincipal principal, BigarrDbContext db, SessionStamps stamps, TimeProvider time, ILoggerFactory logs, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (user.Id == principal.UserId() && disabled)
        {
            return TypedResults.Problem(title: "You cannot disable yourself.", statusCode: StatusCodes.Status409Conflict);
        }

        if (user.IsDisabled != disabled)
        {
            user.DisabledAt = disabled ? time.GetUtcNow().UtcDateTime : null;
            await db.SaveChangesAsync(ct);
            stamps.Changed(user);
            logs.CreateLogger("Bigarr.Auth").LogInformation("User {Username} {State}", user.Username, disabled ? "disabled" : "enabled");
        }

        var logins = await db.ExternalLogins.AsNoTracking().Where(l => l.UserId == id).ToListAsync(ct);
        return TypedResults.Ok(ToResponse(user, logins, principal.UserId()));
    }

    private static async Task<Ok<UserSettingsResponse>> SettingsAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await SettingsResponseAsync(db, ct));

    private static async Task<Ok<UserSettingsResponse>> UpdateSettingsAsync(UpdateUserSettingsRequest request, BigarrDbContext db, CancellationToken ct)
    {
        var settings = await UserSettings.EditAsync(db, ct);
        settings.PlexNewAccounts = request.PlexNewAccounts;
        settings.DefaultPermissions = request.DefaultPermissions.ToDomain();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await SettingsResponseAsync(db, ct));
    }

    private static async Task<UserSettingsResponse> SettingsResponseAsync(BigarrDbContext db, CancellationToken ct)
    {
        var settings = await UserSettings.ReadAsync(db, ct);
        var plexServer = await db.MediaServers.AnyAsync(s => s.Enabled && s.Kind == MediaServerKind.Plex && s.Token != string.Empty, ct);
        return new UserSettingsResponse(settings.PlexNewAccounts, PermissionsDto.From(settings.DefaultPermissions), plexServer);
    }

    private static UserResponse ToResponse(User user, IEnumerable<ExternalLogin> logins, int me) =>
        new(
            user.Id,
            user.Username,
            user.Role,
            PermissionsDto.From(user.Permissions),
            user.DisabledAt,
            user.CreatedAt,
            user.LastLoginAt,
            user.HasPassword,
            [.. logins.OrderBy(l => l.Provider).Select(l => new UserLoginResponse(l.Provider, l.ExternalUsername, l.Email, l.ThumbUrl, l.LastLoginAt))],
            user.Id == me);
}
