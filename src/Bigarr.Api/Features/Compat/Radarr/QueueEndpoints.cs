using System.Security.Claims;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Parsing;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/queue</c> (§7.2): the tracked downloads whose version the key sees, in Radarr's paged envelope. Seerr polls it
/// (with a Sonarr parameter we ignore) to show progress on requests; <c>movieId</c> is the version id.
/// </summary>
public static class QueueEndpoints
{
    public static void MapCompatQueue(this RouteGroupBuilder group)
    {
        group.MapGet("/queue", PageAsync);
        group.MapGet("/queue/details", DetailsAsync);
        group.MapGet("/queue/status", StatusAsync);
        group.MapDelete("/queue/{id:int}", RemoveAsync);
    }

    private static async Task<JsonHttpResult<RadarrPage<RadarrQueueItem>>> PageAsync(
        ClaimsPrincipal user, CompatLibrary library, QueueState queue, TimeProvider time, CancellationToken ct,
        int page = 1, int pageSize = 20, string sortKey = "timeleft", string sortDirection = "ascending", int? movieId = null)
    {
        var items = await VisibleAsync(user, library, queue, time, movieId, ct);
        var ordered = sortDirection.StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? items.OrderByDescending(SortBy(sortKey)).ToList() : items.OrderBy(SortBy(sortKey)).ToList();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var records = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return RadarrCompat.Ok(new RadarrPage<RadarrQueueItem>(page, pageSize, sortKey, sortDirection, ordered.Count, records));
    }

    private static async Task<JsonHttpResult<List<RadarrQueueItem>>> DetailsAsync(
        ClaimsPrincipal user, CompatLibrary library, QueueState queue, TimeProvider time, CancellationToken ct, int? movieId = null) =>
        RadarrCompat.Ok(await VisibleAsync(user, library, queue, time, movieId, ct));

    private static async Task<JsonHttpResult<object>> StatusAsync(ClaimsPrincipal user, CompatLibrary library, QueueState queue, TimeProvider time, CancellationToken ct)
    {
        var items = await VisibleAsync(user, library, queue, time, null, ct);
        return RadarrCompat.Ok<object>(new
        {
            totalCount = items.Count,
            count = items.Count,
            unknownCount = 0,
            errors = items.Any(i => i.TrackedDownloadStatus == "error"),
            warnings = items.Any(i => i.TrackedDownloadStatus == "warning"),
            unknownErrors = false,
            unknownWarnings = false,
        });
    }

    private static async Task<Results<Ok, NotFound, JsonHttpResult<RadarrError>>> RemoveAsync(
        int id, ClaimsPrincipal user, CompatLibrary library, QueueState queue, QueueService service, TimeProvider time, CancellationToken ct,
        bool removeFromClient = true, bool blocklist = false)
    {
        var items = await VisibleAsync(user, library, queue, time, null, ct);
        if (items.All(i => i.Id != id))
        {
            return TypedResults.NotFound();
        }

        var result = await service.RemoveGrabAsync(id, removeFromClient, deleteFiles: removeFromClient, blocklist, ct);
        return result.IsSuccess ? TypedResults.Ok() : RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
    }

    private static async Task<List<RadarrQueueItem>> VisibleAsync(ClaimsPrincipal user, CompatLibrary library, QueueState queue, TimeProvider time, int? movieId, CancellationToken ct)
    {
        var visible = await library.VisibleVersionIdsAsync(user.CompatView(), ct);
        var now = time.GetUtcNow().UtcDateTime;
        return queue.Snapshot.Items
            .Where(i => i.Tracked && i.GrabId is not null && i.VersionId is { } versionId && visible.Contains(versionId) && (movieId is null || versionId == movieId))
            .Select(i => ToResource(i, now))
            .ToList();
    }

    private static Func<RadarrQueueItem, object?> SortBy(string key) => key.ToLowerInvariant() switch
    {
        "title" => i => i.Title,
        "size" => i => i.Size,
        "sizeleft" => i => i.Sizeleft,
        "progress" => i => i.Size == 0 ? 0 : (double)(i.Size - i.Sizeleft) / i.Size,
        "status" => i => i.Status,
        "added" => i => i.Added,
        _ => i => i.EstimatedCompletionTime ?? DateTime.MaxValue,
    };

    public static RadarrQueueItem ToResource(QueueItemResponse item, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(item);

        var eta = item.EtaSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        var worst = item.StatusMessages.Count == 0 ? StatusSeverity.Info : item.StatusMessages.Max(m => m.Severity);
        var trackedStatus = item.State == GrabState.DownloadFailed || worst == StatusSeverity.Error ? "error" : worst == StatusSeverity.Warning ? "warning" : "ok";
        return new RadarrQueueItem(
            item.GrabId!.Value,
            item.VersionId!.Value,
            [],
            RadarrMapper.QualityFromTitle(item.Title),
            [],
            0,
            item.Size,
            item.Title,
            item.SizeLeft,
            eta?.ToString(),
            eta is null ? null : now + eta.Value,
            item.AddedAt,
            Status(item.Status),
            trackedStatus,
            State(item.State),
            item.StatusMessages.Select(m => new RadarrStatusMessage(m.Title, m.Messages)).ToList(),
            item.StatusMessages.FirstOrDefault(m => m.Severity == StatusSeverity.Error)?.Messages.FirstOrDefault(),
            item.DownloadId,
            "torrent",
            item.DownloadClient,
            false,
            item.Indexer,
            item.OutputPath);
    }

    private static string Status(DownloadItemStatus status) => status switch
    {
        DownloadItemStatus.Queued => "queued",
        DownloadItemStatus.Downloading => "downloading",
        DownloadItemStatus.Paused => "paused",
        DownloadItemStatus.Stalled => "warning",
        DownloadItemStatus.Completed => "completed",
        DownloadItemStatus.Failed => "failed",
        _ => "unknown",
    };

    private static string State(GrabState? state) => state switch
    {
        GrabState.ImportPending => "importPending",
        GrabState.Importing => "importing",
        GrabState.Imported => "imported",
        GrabState.ImportFailed => "importBlocked",
        GrabState.DownloadFailed => "failed",
        GrabState.Ignored => "ignored",
        _ => "downloading",
    };
}
