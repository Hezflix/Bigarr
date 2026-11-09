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

        group.MapGet("/plays", async Task<Ok<PlaysPageResponse>> (int? page, int? pageSize, int? userId, int? movieId, int? versionId, MediaItemKind? kind, PlaybackService playback, CancellationToken ct) =>
            TypedResults.Ok(await playback.PlaysAsync(page ?? 1, pageSize ?? 50, userId, movieId, versionId, kind, ct)));

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
        services.AddScoped<VersionResolver>();
        services.AddScoped<PlaybackTracker>();
        services.AddScoped<PlaybackService>();
        services.AddSingleton<PlaybackMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<PlaybackMonitor>());
        return services;
    }
}
