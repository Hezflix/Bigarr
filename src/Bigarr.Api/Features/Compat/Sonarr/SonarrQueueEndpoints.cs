using System.Security.Claims;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

/// <summary>
/// <c>/queue</c>: the tracked series downloads whose version the key sees, in the paged envelope. One record per
/// (download, episode), as Seerr's download tracker reads a single <c>episode</c> per record: a season pack is one
/// record per episode it carries, all sharing its <c>downloadId</c>, <c>title</c> and <c>size</c>. <c>seriesId</c> is the
/// version id. Every record is returned unless a page is asked for: Seerr sends no <c>pageSize</c>.
/// </summary>
public static class SonarrQueueEndpoints
{
    public static void MapSonarrQueue(this RouteGroupBuilder group)
    {
        group.MapGet("/queue", PageAsync);
        group.MapGet("/queue/details", DetailsAsync);
        group.MapGet("/queue/status", StatusAsync);
        group.MapDelete("/queue/{id:int}", RemoveAsync);
    }

    private static async Task<JsonHttpResult<RadarrPage<SonarrQueueItem>>> PageAsync(
        ClaimsPrincipal user, SonarrLibrary library, QueueState queue, TimeProvider time, CancellationToken ct,
        int page = 1, int? pageSize = null, string sortKey = "timeleft", string sortDirection = "ascending", int? seriesId = null)
    {
        var items = await VisibleAsync(user, library, queue, time, seriesId, ct);
        var ordered = sortDirection.StartsWith("desc", StringComparison.OrdinalIgnoreCase) ? items.OrderByDescending(SortBy(sortKey)).ToList() : items.OrderBy(SortBy(sortKey)).ToList();
        page = Math.Max(1, page);
        var size = Math.Clamp(pageSize ?? Math.Max(1, ordered.Count), 1, Math.Max(1000, ordered.Count));
        var records = ordered.Skip((page - 1) * size).Take(size).ToList();
        return SonarrCompat.Ok(new RadarrPage<SonarrQueueItem>(page, size, sortKey, sortDirection, ordered.Count, records));
    }

    private static async Task<JsonHttpResult<List<SonarrQueueItem>>> DetailsAsync(
        ClaimsPrincipal user, SonarrLibrary library, QueueState queue, TimeProvider time, CancellationToken ct, int? seriesId = null) =>
        SonarrCompat.Ok(await VisibleAsync(user, library, queue, time, seriesId, ct));

    private static async Task<JsonHttpResult<object>> StatusAsync(ClaimsPrincipal user, SonarrLibrary library, QueueState queue, TimeProvider time, CancellationToken ct)
    {
        var items = await VisibleAsync(user, library, queue, time, null, ct);
        return SonarrCompat.Ok<object>(new
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

    /// <summary>Removing one episode's record removes its download, as in Sonarr: a pack goes whole.</summary>
    private static async Task<Results<Ok, NotFound, JsonHttpResult<RadarrError>>> RemoveAsync(
        int id, ClaimsPrincipal user, SonarrLibrary library, QueueState queue, QueueService service, TimeProvider time, CancellationToken ct,
        bool removeFromClient = true, bool blocklist = false)
    {
        var grabId = (await VisibleItemsAsync(user, library, queue, null, ct))
            .SelectMany(i => i.Episodes.Select(e => (i.GrabId!.Value, e.EpisodeId)))
            .Where(p => RecordId(p.Item1, p.EpisodeId) == id)
            .Select(p => (int?)p.Item1)
            .FirstOrDefault();
        if (grabId is null)
        {
            return TypedResults.NotFound();
        }

        var result = await service.RemoveGrabAsync(grabId.Value, removeFromClient, deleteFiles: removeFromClient, blocklist, ct);
        return result.IsSuccess ? TypedResults.Ok() : RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Detail ?? result.Failure.Title);
    }

    private static async Task<List<QueueItemResponse>> VisibleItemsAsync(ClaimsPrincipal user, SonarrLibrary library, QueueState queue, int? seriesId, CancellationToken ct)
    {
        var visible = await library.VisibleVersionIdsAsync(user.CompatView(), ct);
        return queue.Snapshot.Items
            .Where(i => i.Tracked && i.Series && i.GrabId is not null && i.VersionId is { } versionId && visible.Contains(versionId) && (seriesId is null || versionId == seriesId))
            .ToList();
    }

    private static async Task<List<SonarrQueueItem>> VisibleAsync(ClaimsPrincipal user, SonarrLibrary library, QueueState queue, TimeProvider time, int? seriesId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return (await VisibleItemsAsync(user, library, queue, seriesId, ct)).SelectMany(i => ToRecords(i, now)).ToList();
    }

    private static Func<SonarrQueueItem, object?> SortBy(string key) => key.ToLowerInvariant() switch
    {
        "title" => i => i.Title,
        "size" => i => i.Size,
        "sizeleft" => i => i.Sizeleft,
        "progress" => i => i.Size == 0 ? 0 : (double)(i.Size - i.Sizeleft) / i.Size,
        "status" => i => i.Status,
        "added" => i => i.Added,
        "episode" or "episodes.airdateutc" => i => (i.SeasonNumber * 100000) + i.Episode.EpisodeNumber,
        _ => i => i.EstimatedCompletionTime ?? DateTime.MaxValue,
    };

    /// <summary>The records of one tracked series download: one per episode, the download's figures repeated on each.</summary>
    public static IEnumerable<SonarrQueueItem> ToRecords(QueueItemResponse item, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(item);

        var download = QueueEndpoints.ToResource(item, now);
        var quality = SonarrQuality(item.Title);
        return item.Episodes.Select(e => new SonarrQueueItem(
            RecordId(item.GrabId!.Value, e.EpisodeId),
            item.VersionId!.Value,
            e.EpisodeId,
            e.SeasonNumber,
            new SonarrQueueEpisode(e.EpisodeId, item.VersionId.Value, e.SeasonNumber, e.EpisodeNumber, e.AbsoluteEpisodeNumber, e.Title, e.HasFile, true),
            [],
            quality,
            [],
            0,
            download.Size,
            download.Title,
            download.Sizeleft,
            download.Timeleft,
            download.EstimatedCompletionTime,
            download.Added,
            download.Status,
            download.TrackedDownloadStatus,
            download.TrackedDownloadState,
            download.StatusMessages,
            download.ErrorMessage,
            download.DownloadId,
            download.Protocol,
            download.DownloadClient,
            download.DownloadClientHasPostImportCategory,
            download.Indexer,
            download.OutputPath,
            e.HasFile));
    }

    /// <summary>
    /// A stable id per (download, episode), like Sonarr's hash of the tracked download and the episode: the same pack
    /// gives its records the same ids at every poll, and a client can delete one by it.
    /// </summary>
    public static int RecordId(int grabId, int episodeId)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var value in new[] { grabId, episodeId })
            {
                for (var shift = 0; shift < 32; shift += 8)
                {
                    hash = (hash ^ (uint)((value >> shift) & 0xFF)) * 16777619u;
                }
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>The quality the episode parser reads in the release title, in Sonarr's shape.</summary>
    public static RadarrQuality SonarrQuality(string title)
    {
        try
        {
            var parsed = Bigarr.Parsing.Episodes.QualityParser.ParseQuality(title);
            if (parsed?.Quality is null)
            {
                return RadarrMapper.ToQuality(null, null);
            }

            var q = parsed.Quality;
            return new RadarrQuality(
                new RadarrQualityDefinition(q.Id, q.Name, q.Source.ToString().ToLowerInvariant(), q.Resolution, RadarrMapper.Camel(q.Modifier.ToString().ToLowerInvariant())),
                new RadarrRevision(parsed.Revision?.Version ?? 1, parsed.Revision?.Real ?? 0, parsed.Revision?.IsRepack ?? false));
        }
        catch (Exception)
        {
            return RadarrMapper.ToQuality(null, null);
        }
    }
}
