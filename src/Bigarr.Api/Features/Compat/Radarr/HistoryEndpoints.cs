using System.Security.Claims;
using Bigarr.Api.Features.History;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.History;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/history</c>: Bazarr reads the grabs of a film (<c>eventType=1</c>, <c>movieIds=</c>) to refine its releases.
/// Our journal is per version; a compat <c>movieId</c> is a version id, so the filter lands on <c>TargetId</c>.
/// </summary>
public static class HistoryEndpoints
{
    public static void MapCompatHistory(this RouteGroupBuilder group)
    {
        group.MapGet("/history", PageAsync);
        group.MapGet("/history/movie", ForMovieAsync);
    }

    private static async Task<JsonHttpResult<RadarrPage<RadarrHistoryRecord>>> PageAsync(
        HttpRequest http, ClaimsPrincipal user, HistoryService history, CancellationToken ct,
        int page = 1, int pageSize = 20, string sortKey = "date", string sortDirection = "descending")
    {
        // The journal is per target: the key's view and the requested ids both land on the event's version, and the database
        // counts and pages. A request for ids the key does not see matches nothing (it does not widen to the whole view).
        var requested = Ints(http.Query["movieIds"]).Concat(Ints(http.Query["movieId"])).ToList();
        var filter = new HistoryFilter(
            VersionIds: requested.Count == 0 ? null : requested,
            EventTypes: EnumTypes(Types(http.Query["eventType"])),
            TargetVersions: CompatLibrary.Predicate(user.CompatView()));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var ascending = sortDirection.StartsWith("asc", StringComparison.OrdinalIgnoreCase);
        var result = await history.PageAsync(filter, page, pageSize, ascending, ct);
        return RadarrCompat.Ok(new RadarrPage<RadarrHistoryRecord>(page, pageSize, sortKey, sortDirection, result.Total, result.Events.Select(ToResource).ToList()));
    }

    /// <summary>Radarr's unpaged variant: the whole history of one movie (version), newest first.</summary>
    private static async Task<Results<JsonHttpResult<List<RadarrHistoryRecord>>, NotFound>> ForMovieAsync(
        HttpRequest http, int movieId, ClaimsPrincipal user, CompatLibrary library, HistoryService history, CancellationToken ct)
    {
        var row = await library.FindAsync(user.CompatView(), movieId, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        var events = await history.ListAsync(new HistoryFilter(VersionId: movieId, EventTypes: EnumTypes(Types(http.Query["eventType"]))), 500, ct);
        return RadarrCompat.Ok(events.Select(ToResource).ToList());
    }

    /// <summary>The journal's event types behind Radarr's names, or null for "no filter". Names nobody maps to match nothing.</summary>
    private static List<HistoryEventType>? EnumTypes(HashSet<string> names) =>
        names.Count == 0 ? null : Enum.GetValues<HistoryEventType>().Where(t => names.Contains(EventType(t))).ToList();

    private static IEnumerable<int> Ints(Microsoft.Extensions.Primitives.StringValues values) =>
        values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Select(v => int.TryParse(v, out var i) ? i : (int?)null).Where(i => i is not null).Select(i => i!.Value);

    /// <summary>Radarr accepts the numeric enum (<c>1</c>) or its name (<c>grabbed</c>).</summary>
    private static HashSet<string> Types(Microsoft.Extensions.Primitives.StringValues values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            result.Add(int.TryParse(value, out var number) ? EventTypeName(number) : value);
        }

        return result;
    }

    private static string EventTypeName(int radarrType) => radarrType switch
    {
        1 => "grabbed",
        3 => "downloadFolderImported",
        4 => "downloadFailed",
        6 => "movieFileDeleted",
        7 => "movieFolderImported",
        8 => "movieFileRenamed",
        9 => "downloadIgnored",
        _ => "unknown",
    };

    public static string EventType(HistoryEventType type) => type switch
    {
        HistoryEventType.Grabbed => "grabbed",
        HistoryEventType.Imported or HistoryEventType.Upgraded => "downloadFolderImported",
        HistoryEventType.DownloadFailed => "downloadFailed",
        HistoryEventType.FileDeleted => "movieFileDeleted",
        HistoryEventType.Renamed => "movieFileRenamed",
        HistoryEventType.Blocklisted => "downloadIgnored",
        _ => "unknown",
    };

    public static RadarrHistoryRecord ToResource(HistoryEvent h)
    {
        ArgumentNullException.ThrowIfNull(h);

        var data = new Dictionary<string, string>(h.Data, StringComparer.Ordinal);
        if (h.IndexerName is not null)
        {
            data.TryAdd("indexer", h.IndexerName);
        }

        if (h.EventType == HistoryEventType.Grabbed && h.SourceTitle is not null)
        {
            var group = ReleaseGroup(h.SourceTitle);
            if (group is not null)
            {
                data.TryAdd("releaseGroup", group);
            }
        }

        return new RadarrHistoryRecord(
            h.Id,
            h.TargetId ?? 0,
            h.SourceTitle,
            [],
            RadarrMapper.QualityFromTitle(h.SourceTitle ?? string.Empty),
            [],
            0,
            false,
            h.CreatedAt,
            data.GetValueOrDefault("downloadId"),
            EventType(h.EventType),
            data);
    }

    private static string? ReleaseGroup(string title)
    {
        try
        {
            return Bigarr.Parsing.Parser.ParseMovieTitle(title)?.ReleaseGroup;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
