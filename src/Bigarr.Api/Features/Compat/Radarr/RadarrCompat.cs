using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// The Radarr v3 compatible surface: what Seerr, Bazarr and Prowlarr call. A foreign contract with its
/// own lifecycle, kept apart from the v1 API: it calls the slices through their public services, applies the API
/// key's view, and translates to the *arr shape. It never touches EF directly, which is what keeps it disposable.
/// </summary>
public static class RadarrCompat
{
    public const string RoutePrefix = "/api/v3";

    /// <summary>
    /// The Radarr version announced in <c>/system/status</c> and in the <see cref="VersionHeader"/> header. Bazarr treats
    /// 0.x and 3.x as deprecated, Prowlarr requires at least 4.0.4.0 (parsed as a <see cref="Version"/>, digits only).
    /// </summary>
    public const string ApplicationVersion = "5.99.0.0";

    public const string VersionHeader = "X-Application-Version";

    /// <summary>
    /// Radarr's JSON leaves null fields out (Newtonsoft with <c>NullValueHandling.Ignore</c>), and clients rely on it:
    /// Bazarr skips every movie whose <c>movieFile</c> key is absent, and would choke on an explicit null.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static JsonHttpResult<T> Ok<T>(T value) => TypedResults.Json(value, Json);

    public static JsonHttpResult<T> Created<T>(T value) => TypedResults.Json(value, Json, statusCode: StatusCodes.Status201Created);

    /// <summary>Radarr's 400: a list of validation failures, not a problem document.</summary>
    public static JsonHttpResult<List<RadarrValidationFailure>> BadRequest(string property, string message) =>
        TypedResults.Json(new List<RadarrValidationFailure> { new(property, message) }, Json, statusCode: StatusCodes.Status400BadRequest);

    public static JsonHttpResult<RadarrError> Error(int statusCode, string message) =>
        TypedResults.Json(new RadarrError(message), Json, statusCode: statusCode);

    /// <summary>The <c>X-Application-Version</c> header on every compat response, including errors: Prowlarr reads it on <c>POST /indexer/test</c>.</summary>
    public static IApplicationBuilder UseRadarrCompat(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(RoutePrefix) || context.Request.Path.StartsWithSegments("/signalr"))
            {
                context.Response.OnStarting(static state =>
                {
                    ((HttpContext)state).Response.Headers[VersionHeader] = ApplicationVersion;
                    return Task.CompletedTask;
                }, context);
            }

            await next(context);
        });
        return app;
    }
}

public sealed record RadarrValidationFailure(string PropertyName, string ErrorMessage, string Severity = "error");

public sealed record RadarrError(string Message);

public sealed record RadarrPage<T>(int Page, int PageSize, string SortKey, string SortDirection, int TotalRecords, List<T> Records);
