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

        // sort is matched by name, case-insensitive (startedAt, user, title, version, played); newest first by default.
        group.MapGet("/plays", async Task<Results<Ok<PlaysPageResponse>, ProblemHttpResult>> (int? page, int? pageSize, int? userId, int? movieId, int? versionId, MediaItemKind? kind, string? sort, string? direction, PlaybackService playback, CancellationToken ct) =>
        {
            var key = PlaySort.StartedAt;
            if (sort is not null && (!Enum.TryParse(sort, ignoreCase: true, out key) || !Enum.IsDefined(key)))
            {
                return TypedResults.Problem(title: "Unknown sort", detail: "Expected startedAt, user, title, version or played.", statusCode: StatusCodes.Status400BadRequest);
            }

            var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);
            return TypedResults.Ok(await playback.PlaysAsync(page ?? 1, pageSize ?? 50, userId, movieId, versionId, kind, key, ascending, ct));
        });

        group.MapGet("/plays/{id:int}/streams", async Task<Results<Ok<List<PlayStreamResponse>>, NotFound>> (int id, PlaybackService playback, CancellationToken ct) =>
            await playback.StreamsAsync(id, ct) is { } streams ? TypedResults.Ok(streams) : TypedResults.NotFound());

        group.MapGet("/users", async Task<Ok<List<PlaybackUserResponse>>> (PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.UsersAsync(ct)));

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

    public static IServiceCollection AddBigarrPlayback(this IServiceCollection services)
    {
        services.AddSingleton<PlexLibraries>();
        services.AddSingleton<LiveTranscodes>();
        services.AddScoped<VersionResolver>();
        services.AddScoped<PlaybackTracker>();
        services.AddScoped<PlaybackService>();
        services.AddSingleton<PlaybackMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<PlaybackMonitor>());
        return services;
    }
}
