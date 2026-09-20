using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.History;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.History;

public sealed record HistoryEventResponse(
    int Id,
    HistoryEventType EventType,
    int? MovieId,
    int? TmdbId,
    string? MovieTitle,
    int? VersionId,
    string? VersionName,
    string? SourceTitle,
    int? IndexerId,
    string? IndexerName,
    int? MediaFileId,
    Dictionary<string, string> Data,
    DateTime CreatedAt);

/// <summary>Read side of the journal. Events are written by the slices that do the work, in their own transaction.</summary>
public static class HistoryEndpoints
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/history").WithTags("History").RequireAuthorization(AuthPolicies.Api);

        // eventType is matched by name, case-insensitive: the front sends camelCase like the JSON it reads.
        group.MapGet("/", async Task<Results<Ok<List<HistoryEventResponse>>, ValidationProblem>> (int? movieId, int? versionId, string? eventType, int? limit, HistoryService history, CancellationToken ct) =>
        {
            HistoryEventType? type = null;
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                if (!Enum.TryParse<HistoryEventType>(eventType, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["eventType"] = [$"Unknown event type '{eventType}'."] });
                }

                type = parsed;
            }

            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var events = await history.ListAsync(new HistoryFilter(movieId, null, versionId, type is null ? null : [type.Value]), take, ct);
            return TypedResults.Ok(events.Select(ToResponse).ToList());
        });

        return app;
    }

    public static HistoryEventResponse ToResponse(HistoryEvent h) => new(
        h.Id, h.EventType, h.MovieId, h.TmdbId, h.MovieTitle, h.TargetId, h.VersionName, h.SourceTitle, h.IndexerId, h.IndexerName, h.MediaFileId, h.Data, h.CreatedAt);
}
