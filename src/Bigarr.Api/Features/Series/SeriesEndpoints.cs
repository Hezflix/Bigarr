namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

public static class SeriesEndpoints
{
    public static IEndpointRouteBuilder MapSeries(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/series").WithTags("Series").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", ListAsync);
        group.MapGet("/lookup", LookupAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapGet("/{id:int}/episodes", EpisodesAsync);
        group.MapPost("/", AddAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/{id:int}/refresh", RefreshAsync);
        group.MapPost("/{id:int}/versions", AddVersionAsync);
        group.MapPut("/{id:int}/versions/{versionId:int}", UpdateVersionAsync);
        group.MapDelete("/{id:int}/versions/{versionId:int}", DeleteVersionAsync);
        group.MapPost("/{id:int}/versions/{versionId:int}/promote", PromoteAsync);
        group.MapPost("/{id:int}/versions/{versionId:int}/monitoring", ApplyMonitoringAsync);
        group.MapPut("/{id:int}/versions/{versionId:int}/seasons/{seasonNumber:int}", SetSeasonAsync);
        group.MapPut("/{id:int}/versions/{versionId:int}/episodes", SetEpisodesAsync);

        return app;
    }

    private static async Task<Ok<List<SeriesSummary>>> ListAsync(SeriesQueries queries, CancellationToken ct) =>
        TypedResults.Ok(await queries.ListAsync(ct));

    private static async Task<Results<Ok<List<SeriesLookupResult>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        string? term, SeriesService series, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["term"] = ["A search term is required."] });
        }

        var result = await series.LookupAsync(term, ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value!.ToList()) : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<SeriesResponse>, NotFound>> GetAsync(int id, SeriesQueries queries, CancellationToken ct) =>
        await queries.GetAsync(id, ct) is { } series ? TypedResults.Ok(series) : TypedResults.NotFound();

    private static async Task<Results<Ok<List<EpisodeResponse>>, NotFound>> EpisodesAsync(int id, SeriesQueries queries, CancellationToken ct, int? seasonNumber = null) =>
        await queries.EpisodesAsync(id, seasonNumber, ct) is { } episodes ? TypedResults.Ok(episodes) : TypedResults.NotFound();

    private static async Task<Results<Created<SeriesResponse>, ProblemHttpResult>> AddAsync(AddSeriesRequest request, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.AddSeriesAsync(request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var id = result.Value!.Id;
        return TypedResults.Created($"/api/v1/series/{id}", (await queries.GetAsync(id, ct))!);
    }

    private static async Task<Results<Ok<SeriesResponse>, ProblemHttpResult>> UpdateAsync(int id, UpdateSeriesRequest request, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.UpdateSeriesAsync(id, request, ct);
        return result.IsSuccess ? TypedResults.Ok((await queries.GetAsync(id, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(int id, SeriesService series, CancellationToken ct)
    {
        var result = await series.DeleteSeriesAsync(id, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<SeriesResponse>, ProblemHttpResult>> RefreshAsync(int id, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.RefreshAsync(id, ct);
        return result.IsSuccess ? TypedResults.Ok((await queries.GetAsync(id, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<Created<SeriesVersionResponse>, ProblemHttpResult>> AddVersionAsync(
        int id, AddSeriesVersionRequest request, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.AddVersionAsync(id, request, ct);
        if (!result.IsSuccess)
        {
            return result.Failure!.ToProblem();
        }

        var versionId = result.Value!.Id;
        return TypedResults.Created($"/api/v1/series/{id}/versions/{versionId}", (await queries.GetVersionAsync(id, versionId, ct))!);
    }

    private static async Task<Results<Ok<SeriesVersionResponse>, ProblemHttpResult>> UpdateVersionAsync(
        int id, int versionId, UpdateSeriesVersionRequest request, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.UpdateVersionAsync(id, versionId, request, ct);
        return result.IsSuccess ? TypedResults.Ok((await queries.GetVersionAsync(id, versionId, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteVersionAsync(int id, int versionId, SeriesService series, CancellationToken ct, int? promote = null)
    {
        var result = await series.DeleteVersionAsync(id, versionId, promote, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<SeriesVersionResponse>, ProblemHttpResult>> PromoteAsync(int id, int versionId, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.PromoteAsync(id, versionId, ct);
        return result.IsSuccess ? TypedResults.Ok((await queries.GetVersionAsync(id, versionId, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<Ok<SeriesResponse>, ProblemHttpResult>> ApplyMonitoringAsync(
        int id, int versionId, ApplyMonitoringRequest request, SeriesService series, SeriesQueries queries, CancellationToken ct)
    {
        var result = await series.ApplyMonitoringAsync(id, versionId, request.Monitor, ct);
        return result.IsSuccess ? TypedResults.Ok((await queries.GetAsync(id, ct))!) : result.Failure!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetSeasonAsync(
        int id, int versionId, int seasonNumber, SeasonMonitoringRequest request, SeriesService series, CancellationToken ct)
    {
        var result = await series.SetSeasonMonitoredAsync(id, versionId, seasonNumber, request.Monitored, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetEpisodesAsync(
        int id, int versionId, EpisodeMonitoringRequest request, SeriesService series, CancellationToken ct)
    {
        var result = await series.SetEpisodesMonitoredAsync(id, versionId, request.EpisodeIds ?? [], request.Monitored, ct);
        return result.IsSuccess ? TypedResults.NoContent() : result.Failure!.ToProblem();
    }
}
