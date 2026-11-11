namespace Bigarr.Api.Features.SeriesFilms;

using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public static class SeriesFilmEndpoints
{
    public static IEndpointRouteBuilder MapSeriesFilms(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Series films").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/series/{id:int}/films", ListAsync);
        group.MapPost("/series/{id:int}/films", AddAsync);
        group.MapPost("/series/{id:int}/films/detect", DetectAsync);
        group.MapDelete("/series/{id:int}/films/{episodeId:int}", RemoveAsync);
        group.MapGet("/movies/{id:int}/series-links", ForMovieAsync);
        group.MapPut("/settings/series-films", SettingsAsync);
        group.MapPost("/series-films/links/reconcile", ReconcileAsync);

        return app;
    }

    private static async Task<Results<Ok<SeriesFilmsResponse>, NotFound>> ListAsync(int id, SeriesFilmService films, CancellationToken ct) =>
        await films.ListAsync(id, ct) is { } list ? TypedResults.Ok(list) : TypedResults.NotFound();

    private static async Task<Results<Ok<SeriesFilmsResponse>, ProblemHttpResult>> AddAsync(int id, AddSeriesFilmRequest request, SeriesFilmService films, CancellationToken ct)
    {
        var result = await films.AddAsync(id, request, ct);
        return result.IsSuccess ? TypedResults.Ok((await films.ListAsync(id, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<SeriesFilmsResponse>, NotFound>> DetectAsync(int id, SeriesFilmService films, CancellationToken ct)
    {
        await films.DetectAsync(id, ct);
        return await films.ListAsync(id, ct) is { } list ? TypedResults.Ok(list) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(int id, int episodeId, SeriesFilmService films, CancellationToken ct)
    {
        var result = await films.RemoveAsync(id, episodeId, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Ok<List<MovieSeriesLinkResponse>>> ForMovieAsync(int id, SeriesFilmService films, CancellationToken ct) =>
        TypedResults.Ok(await films.ForMovieAsync(id, ct));

    private static async Task<NoContent> SettingsAsync(SeriesFilmSettingsRequest request, SeriesFilmService films, CancellationToken ct)
    {
        await films.SetAutoAddAsync(request.AutoAdd, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Runs the link keeper now, over every link (what the scheduled job does). Shadow mode touches nothing.</summary>
    private static async Task<Ok<LinkPass>> ReconcileAsync(SeriesFilmLinker linker, Microsoft.Extensions.Options.IOptions<Shadow.ShadowOptions> shadow, CancellationToken ct) =>
        TypedResults.Ok(shadow.Value.Enabled ? new LinkPass(0, 0, 0, 0) : await linker.ReconcileAllAsync(ct));
}
