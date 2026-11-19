using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Auth;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddBigarrAuthentication(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<SessionStamps>();
        services.AddSingleton<External.ExternalSignInFlows>();
        services.AddScoped<External.IExternalIdentityProvider, External.PlexIdentityProvider>();

        services.AddAuthentication(AuthSchemes.Session)
            .AddCookie(AuthSchemes.Session, o =>
            {
                o.Cookie.Name = "bigarr_session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);

                // An API, not a website: never redirect, answer with status codes.
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = ctx =>
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },

                    // A session opened with a password that has since changed is over, and so is one of a user that is
                    // disabled or gone: changing the password is how a session someone else got hold of is ended. A
                    // session whose role or permissions changed goes on with the current ones.
                    OnValidatePrincipal = async ctx =>
                    {
                        var stamps = ctx.HttpContext.RequestServices.GetRequiredService<SessionStamps>();
                        var db = ctx.HttpContext.RequestServices.GetRequiredService<Persistence.BigarrDbContext>();
                        var stamp = ctx.Principal?.FindFirst(BigarrClaims.SessionStamp)?.Value;
                        var ct = ctx.HttpContext.RequestAborted;
                        var state = ctx.Principal is null ? null : await stamps.CurrentAsync(ctx.Principal.UserId(), stamp, db, ct);
                        if (state is null)
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync(AuthSchemes.Session);
                            return;
                        }

                        if (!SessionStamps.Matches(ctx.Principal!, state))
                        {
                            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == ctx.Principal!.UserId(), ct);
                            ctx.ReplacePrincipal(BigarrPrincipal.ForUser(user, AuthSchemes.Session));
                            ctx.ShouldRenew = true;
                        }
                    },
                };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });

        var admin = nameof(UserRole.Admin);
        var adminApi = new AuthorizationPolicyBuilder(AuthSchemes.Session, AuthSchemes.ApiKey).RequireAuthenticatedUser().RequireRole(admin).Build();
        var builder = services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Session, p => p.AddAuthenticationSchemes(AuthSchemes.Session).RequireAuthenticatedUser().RequireRole(admin))
            // A key opens these surfaces only while its owner is an admin.
            .AddPolicy(AuthPolicies.ApiKey, p => p.AddAuthenticationSchemes(AuthSchemes.ApiKey).RequireAuthenticatedUser().RequireRole(admin))
            .AddPolicy(AuthPolicies.Api, adminApi)
            .AddPolicy(AuthPolicies.SignedIn, p => p.AddAuthenticationSchemes(AuthSchemes.Session, AuthSchemes.ApiKey).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.ManageRequests, p => p.AddAuthenticationSchemes(AuthSchemes.Session, AuthSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .RequireRole(admin, nameof(UserRole.RequestManager)))

            // Default deny: an endpoint that names no policy is the admin's. A request that matched no endpoint is left
            // alone, so an unknown address still answers 404.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(AuthSchemes.Session, AuthSchemes.ApiKey)
                .RequireAssertion(ctx => (ctx.Resource is HttpContext http && http.GetEndpoint() is null)
                    || (ctx.User.Identity?.IsAuthenticated == true && ctx.User.IsInRole(admin)))
                .Build());

        foreach (var permission in Enum.GetValues<UserPermission>())
        {
            builder.AddPolicy(AuthPolicies.Has(permission), p => p.AddAuthenticationSchemes(AuthSchemes.Session, AuthSchemes.ApiKey)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx => ctx.User.Has(permission)));
        }

        return services;
    }
}
