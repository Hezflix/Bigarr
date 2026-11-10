using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Playback;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Playback;

public static class PlaybackEndpoints
{
    public static IEndpointRouteBuilder MapPlayback(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/playback").WithTags("Playback").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/sessions", async Task<Ok<NowPlayingResponse>> (PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.NowPlayingAsync(ct)));

        // Stops a stream on its player: the one playback call that changes something on Plex, made only from this button.
        group.MapPost("/sessions/{id:int}/stop", async Task<Results<NoContent, ProblemHttpResult>> (int id, StopStreamRequest? request, PlaybackService playback, CancellationToken ct) =>
        {
            var result = await playback.StopAsync(id, request?.Message, ct);
            return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
        });

        // sort is matched by name, case-insensitive (startedAt, user, title, version, played); newest first by default.
        // from/to bound the start (UTC, to excluded); version is a version name; decision the play's furthest one; q a title.
        group.MapGet("/plays", async Task<Results<Ok<PlaysPageResponse>, ProblemHttpResult>> (int? page, int? pageSize, int? userId, int? movieId, int? versionId, string? version, string? kind, bool? unmatched, DateTime? from, DateTime? to, string? decision, string? q, string? sort, string? direction, PlaybackService playback, CancellationToken ct) =>
        {
            var key = PlaySort.StartedAt;
            if (sort is not null && (!Enum.TryParse(sort, ignoreCase: true, out key) || !Enum.IsDefined(key)))
            {
                return TypedResults.Problem(title: "Unknown sort", detail: "Expected startedAt, user, title, version or played.", statusCode: StatusCodes.Status400BadRequest);
            }

            // Enums by name, case-insensitive: the web sends camelCase (movie, directPlay), like the JSON it reads.
            if (!TryParse<MediaItemKind>(kind, out var itemKind))
            {
                return TypedResults.Problem(title: "Unknown kind", detail: "Expected movie or episode.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryParse<StreamDecision>(decision, out var streamDecision))
            {
                return TypedResults.Problem(title: "Unknown decision", detail: "Expected directPlay, directStream or transcode.", statusCode: StatusCodes.Status400BadRequest);
            }

            var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
            var filter = new PlayFilter(userId, movieId, versionId, version, itemKind, Utc(from), Utc(to), streamDecision, q, unmatched == true);
            return TypedResults.Ok(await playback.PlaysAsync(page ?? 1, pageSize ?? 50, filter, key, ascending, ct));
        });

        group.MapGet("/plays/versions", async Task<Ok<List<string>>> (PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.VersionNamesAsync(ct)));

        group.MapGet("/plays/{id:int}/streams", async Task<Results<Ok<List<PlayStreamResponse>>, NotFound>> (int id, PlaybackService playback, CancellationToken ct) =>
            await playback.StreamsAsync(id, ct) is { } streams ? TypedResults.Ok(streams) : TypedResults.NotFound());

        // Links a film the server played to a Bigarr film by hand (movieId null unlinks it): all its plays follow.
        group.MapPut("/items/{id:int}/link", async Task<Results<Ok<ItemLinkResponse>, ProblemHttpResult>> (int id, LinkItemRequest request, ItemLinks links, CancellationToken ct) =>
        {
            var result = await links.LinkAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapGet("/users", async Task<Ok<List<PlaybackUserResponse>>> (PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.UsersAsync(ct)));

        group.MapGet("/users/{id:int}/addresses", async Task<Results<Ok<List<PlaybackAddressResponse>>, NotFound>> (int id, PlaybackService playback, CancellationToken ct) =>
            await playback.AddressesAsync(id, ct) is { } addresses ? TypedResults.Ok(addresses) : TypedResults.NotFound());

        group.MapGet("/users/{id:int}", async Task<Results<Ok<PlaybackUserStatsResponse>, NotFound>> (int id, PlaybackService playback, CancellationToken ct) =>
            await playback.UserStatsAsync(id, ct) is { } stats ? TypedResults.Ok(stats) : TypedResults.NotFound());

        group.MapPut("/users/{id:int}", async Task<Results<Ok<PlaybackUserStatsResponse>, ProblemHttpResult>> (int id, UpdatePlaybackUserRequest request, PlaybackService playback, CancellationToken ct) =>
        {
            var result = await playback.UpdateUserAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        // days: the last N days (1 to 3650), or since the start when absent; userId: one person's; tz: the viewer's IANA time zone.
        group.MapGet("/stats", async Task<Results<Ok<PlaybackStatsResponse>, ProblemHttpResult>> (int? days, int? userId, string? tz, PlaybackStats stats, CancellationToken ct) =>
            days is < 1 or > 3650
                ? TypedResults.Problem(title: "Invalid period", detail: "Between 1 and 3650 days.", statusCode: StatusCodes.Status400BadRequest)
                : TypedResults.Ok(await stats.StatsAsync(days, userId, PlaybackStats.Zone(tz), ct)));

        group.MapGet("/movies/{movieId:int}", async Task<Ok<List<VersionPlaysResponse>>> (int movieId, PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.MoviePlaysAsync(movieId, ct)));

        group.MapGet("/settings", async Task<Ok<PlaybackSettingsDto>> (PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.SettingsAsync(ct)));

        group.MapPut("/settings", async Task<Results<Ok<PlaybackSettingsDto>, ProblemHttpResult>> (PlaybackSettingsDto request, PlaybackService playback, CancellationToken ct) =>
        {
            var result = await playback.UpdateSettingsAsync(request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }

    private static bool TryParse<T>(string? value, out T? parsed)
        where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (!Enum.TryParse<T>(value, ignoreCase: true, out var result) || !Enum.IsDefined(result) || int.TryParse(value, out _))
        {
            return false;
        }

        parsed = result;
        return true;
    }

    /// <summary>A date without an offset is taken as UTC, like every date Bigarr stores.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        { Kind: DateTimeKind.Unspecified } unspecified => DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
        { } utc => utc,
    };

    public static IServiceCollection AddBigarrPlayback(this IServiceCollection services)
    {
        services.AddSingleton<PlexLibraries>();
        services.AddSingleton<LiveTranscodes>();
        services.AddSingleton<GeoLocator>();
        services.AddScoped<VersionResolver>();
        services.AddScoped<PlaybackTracker>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<PlaybackStats>();
        services.AddScoped<ItemLinks>();
        services.AddScoped<PlexHistoryBackstop>();
        services.AddScoped<IScheduledJob, PlexHistoryJob>();
        services.AddScoped<ServerAccess>();
        services.AddScoped<IScheduledJob, ServerAccessJob>();
        services.AddSingleton<PlaybackMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<PlaybackMonitor>());
        return services;
    }
}
