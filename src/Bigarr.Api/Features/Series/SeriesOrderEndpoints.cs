namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

/// <summary>The per-show order (S6): list the orders, preview a switch, apply it, undo it; the manual fix of a file.</summary>
public static class SeriesOrderEndpoints
{
    public static IEndpointRouteBuilder MapSeriesOrders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/series/{id:int}").WithTags("Series").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/orders", async Task<Results<Ok<SeriesOrdersResponse>, ProblemHttpResult>> (int id, SeriesOrderService orders, CancellationToken ct) =>
        {
            var result = await orders.OrdersAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/orders/preview", async Task<Results<Ok<OrderPreviewResponse>, ProblemHttpResult>> (int id, OrderChoiceRequest request, SeriesOrderService orders, CancellationToken ct) =>
        {
            var result = await orders.PreviewAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/orders/apply", async Task<Results<Ok<OrderApplyResponse>, ProblemHttpResult>> (int id, OrderChoiceRequest request, SeriesOrderService orders, CancellationToken ct) =>
        {
            var result = await orders.ApplyAsync(id, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/orders/undo", async Task<Results<Ok<OrderUndoResponse>, ProblemHttpResult>> (int id, SeriesOrderService orders, CancellationToken ct) =>
        {
            var result = await orders.UndoAsync(id, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        group.MapPost("/episodefiles/{fileId:int}/reassign", async Task<Results<Ok<OrderFileResult>, ProblemHttpResult>> (int id, int fileId, ReassignEpisodeFileRequest request, SeriesOrderService orders, CancellationToken ct) =>
        {
            var result = await orders.ReassignFileAsync(id, fileId, request, ct);
            return result.IsSuccess ? TypedResults.Ok(result.Value!) : result.Failure!.ToProblem();
        });

        return app;
    }
}
