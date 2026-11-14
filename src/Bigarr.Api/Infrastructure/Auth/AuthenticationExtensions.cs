using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

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

                    // A session opened with a password that has since changed is over (and so is one of a user that
                    // is gone): changing the password is how a session someone else got hold of is ended.
                    OnValidatePrincipal = async ctx =>
                    {
                        var stamps = ctx.HttpContext.RequestServices.GetRequiredService<SessionStamps>();
                        var db = ctx.HttpContext.RequestServices.GetRequiredService<Persistence.BigarrDbContext>();
                        var stamp = ctx.Principal?.FindFirst(BigarrClaims.SessionStamp)?.Value;
                        if (ctx.Principal is null || !await stamps.IsCurrentAsync(ctx.Principal.UserId(), stamp, db, ctx.HttpContext.RequestAborted))
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync(AuthSchemes.Session);
                        }
                    },
                };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(AuthSchemes.ApiKey, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Session, p => p.AddAuthenticationSchemes(AuthSchemes.Session).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.ApiKey, p => p.AddAuthenticationSchemes(AuthSchemes.ApiKey).RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Api, p => p.AddAuthenticationSchemes(AuthSchemes.Session, AuthSchemes.ApiKey).RequireAuthenticatedUser());

        return services;
    }
}
