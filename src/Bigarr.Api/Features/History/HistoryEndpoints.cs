using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.History;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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
    DateTime CreatedAt,
    /// <summary>An episode line: its series (with its TVDB id and title as snapshots); <see cref="VersionId"/> is then the episode target.</summary>
    int? SeriesId = null,
    int? TvdbId = null,
    string? SeriesTitle = null);

/// <summary>Read side of the journal. Events are written by the slices that do the work, in their own transaction.</summary>
public static class HistoryEndpoints
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapHistory(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/history").WithTags("History").RequireAuthorization(AuthPolicies.Api);

        // eventType is matched by name, case-insensitive: the front sends camelCase like the JSON it reads.
        // episodeId: every line about the episode, whatever the version (its targets), like a film's history.
        group.MapGet("/", async Task<Results<Ok<List<HistoryEventResponse>>, ValidationProblem>> (
            int? movieId, int? seriesId, int? versionId, string? eventType, int? limit, HistoryService history, BigarrDbContext db, CancellationToken ct, int? episodeId = null) =>
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

            List<int>? targets = null;
            if (episodeId is { } episode)
            {
                targets = await db.EpisodeTargets.AsNoTracking().Where(t => t.EpisodeId == episode).Select(t => t.Id).ToListAsync(ct);
                if (targets.Count == 0)
                {
                    return TypedResults.Ok(new List<HistoryEventResponse>());
                }
            }

            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var events = await history.ListAsync(new HistoryFilter(movieId, null, versionId, type is null ? null : [type.Value], targets, SeriesId: seriesId), take, ct);
            return TypedResults.Ok(events.Select(ToResponse).ToList());
        });

        return app;
    }

    public static HistoryEventResponse ToResponse(HistoryEvent h) => new(
        h.Id, h.EventType, h.MovieId, h.TmdbId, h.MovieTitle, h.TargetId, h.VersionName, h.SourceTitle, h.IndexerId, h.IndexerName, h.MediaFileId, h.Data, h.CreatedAt, h.SeriesId, h.TvdbId, h.SeriesTitle);
}
