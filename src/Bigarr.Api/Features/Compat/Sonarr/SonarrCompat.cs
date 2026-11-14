using Bigarr.Api.Features.Compat.Radarr;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

/// <summary>
/// The Sonarr v3 compatible surface: what Seerr, Bazarr and Prowlarr call when they talk to a Sonarr. Same principles
/// as <see cref="RadarrCompat"/> (a foreign contract kept apart from the v1 API, the API key's view applied before
/// mapping), served under its own URL base, <see cref="UrlBase"/>: Sonarr and Radarr both answer <c>/api/v3/system/status</c>,
/// <c>/api/v3/queue</c> or <c>/signalr/messages</c>, so one Bigarr behind one URL can only be both apps if each has its
/// own base. Every client takes one (Seerr's "URL Base", Bazarr's "Base URL", the path in Prowlarr's Sonarr server URL),
/// which is how a real Sonarr behind a reverse proxy is set up too.
/// </summary>
public static class SonarrCompat
{
    /// <summary>The URL base this surface announces in <c>/system/status</c> and prefixes to its image URLs, as a Sonarr with a URL base does.</summary>
    public const string UrlBase = "/sonarr";

    public const string RoutePrefix = UrlBase + "/api/v3";

    /// <summary>
    /// The Sonarr version announced in <c>/system/status</c> and in the <c>X-Application-Version</c> header. A 4.x at or
    /// above 4.0.9.2421 keeps Bazarr on its <c>/signalr/messages</c> client and on episode files embedded in
    /// <c>/episode</c>, and keeps Seerr and Bazarr off the language profiles of Sonarr 3; Prowlarr wants at least 3.0.5.0.
    /// </summary>
    public const string ApplicationVersion = "4.99.0.0";

    public static JsonHttpResult<T> Ok<T>(T value) => RadarrCompat.Ok(value);

    public static JsonHttpResult<T> Created<T>(T value) => RadarrCompat.Created(value);

    /// <summary>Sonarr answers its updates with 202, like Radarr.</summary>
    public static JsonHttpResult<T> Accepted<T>(T value) => TypedResults.Json(value, RadarrCompat.Json, statusCode: StatusCodes.Status202Accepted);

    /// <summary>The <c>X-Application-Version</c> header on every response of the surface, errors included: Prowlarr reads it on <c>POST /indexer/test</c>.</summary>
    public static IApplicationBuilder UseSonarrCompat(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(UrlBase))
            {
                context.Response.OnStarting(static state =>
                {
                    ((HttpContext)state).Response.Headers[RadarrCompat.VersionHeader] = ApplicationVersion;
                    return Task.CompletedTask;
                }, context);
            }

            await next(context);
        });
        return app;
    }
}
