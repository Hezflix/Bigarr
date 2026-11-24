using System.Text.Encodings.Web;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string HeaderName = "X-Api-Key";

    public const string QueryName = "apikey";

    /// <summary>What SignalR clients (Bazarr) put in the query string of <c>/signalr/messages</c>.</summary>
    public const string AccessTokenQueryName = "access_token";
}

/// <summary>Seerr and Bazarr send the key in the query string, Prowlarr in the header, SignalR clients as <c>access_token</c>: all are accepted.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = Request.Headers[ApiKeyAuthenticationOptions.HeaderName].FirstOrDefault()
                  ?? Request.Query[ApiKeyAuthenticationOptions.QueryName].FirstOrDefault()
                  ?? Request.Query[ApiKeyAuthenticationOptions.AccessTokenQueryName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ApiKeyHasher.Hash(raw.Trim());
        var db = Context.RequestServices.GetRequiredService<BigarrDbContext>();
        var key = await db.ApiKeys
            .AsNoTracking()
            .Include(k => k.User)
            // A disabled user's keys stop working with their sessions.
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null && k.User.DisabledAt == null, Context.RequestAborted);

        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        if (key.LastUsedAt is null || now - key.LastUsedAt.Value > LastUsedThrottle)
        {
            await db.ApiKeys
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), Context.RequestAborted);
        }

        var principal = BigarrPrincipal.ForUser(key.User, Scheme.Name, key);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// A key the owner may use but whose scope does not open this route: say so, so whoever runs the script knows what to
    /// change. A refused session or a refused role keeps the plain 403. The key itself never goes in the body.
    /// </summary>
    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Context.User.KeyScope() is not { } scope || Response.HasStarted)
        {
            await base.HandleForbiddenAsync(properties);
            return;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This API key's scope does not open this route",
                detail: $"The key's scope is '{scope.ToString().ToLowerInvariant()}'. An admin can change it in Settings → Connect.")
            .ExecuteAsync(Context);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "ApiKey";
        return Task.CompletedTask;
    }
}
