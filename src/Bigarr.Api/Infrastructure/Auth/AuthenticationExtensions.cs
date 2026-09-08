using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Bigarr.Api.Infrastructure.Auth;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddBigarrAuthentication(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

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
