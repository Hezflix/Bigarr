using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>
/// A browser sends the session cookie to Bigarr's address from any page, including a page of another application on the
/// same host (another port, a sibling subdomain), which <c>SameSite=Lax</c> does not stop. A request that changes
/// something and carries the session cookie must therefore say, through headers a page cannot forge, that it comes from
/// Bigarr's own origin. Requests made with an API key carry no cookie and are not concerned.
/// </summary>
public static class SameOriginGuard
{
    private const string Fetch = "Sec-Fetch-Site";

    /// <summary>Null when the request may go on; otherwise why it is refused (for the log).</summary>
    public static string? Refusal(string method, bool hasSessionCookie, string? secFetchSite, string? origin, string host)
    {
        if (!hasSessionCookie || HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return null;
        }

        // The browser's own verdict wins: it is set by the browser and a page cannot change it.
        if (!string.IsNullOrEmpty(secFetchSite))
        {
            return secFetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase) || secFetchSite.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"the browser says the request is {secFetchSite}";
        }

        // Neither header: not a browser (curl, a script), so not a forgery through one.
        if (string.IsNullOrEmpty(origin))
        {
            return null;
        }

        // The scheme is not compared: a proxy that ends TLS without forwarding it would make every request look foreign.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return $"the origin '{origin}' is not an address";
        }

        return uri.Authority.Equals(host, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"the origin {uri.Authority} is not {host}";
    }

    public static IApplicationBuilder UseSameOriginGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.RequestServices.GetRequiredService<IOptions<SameOriginOptions>>().Value.RequireSameOrigin)
            {
                await next(context);
                return;
            }

            var request = context.Request;
            var site = request.Headers[Fetch].ToString();
            var origin = request.Headers.Origin.ToString();
            var host = request.Host.Value ?? string.Empty;
            var reason = Refusal(
                request.Method,
                request.Cookies.ContainsKey(AuthSchemes.SessionCookie),
                site.Length > 0 ? site : null,
                origin.Length > 0 ? origin : null,
                host);
            if (reason is null)
            {
                await next(context);
                return;
            }

            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Bigarr.Auth").LogWarning(
                "Refused {Method} {Path}: {Reason} (Sec-Fetch-Site {Site}, Origin {Origin}, Host {Host})",
                request.Method,
                request.Path,
                reason,
                site,
                origin,
                host);
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "This request did not come from Bigarr",
                detail: "A request that changes something with a browser session must be sent by Bigarr's own pages. Behind a reverse proxy, forward the Host header (or X-Forwarded-Host)."
            ).ExecuteAsync(context);
        });
}
