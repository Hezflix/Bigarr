using System.Security.Claims;
using Bigarr.Api.Features.History;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.History;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>
/// <c>/history</c> (§7.3): Bazarr reads the grabs of a film (<c>eventType=1</c>, <c>movieIds=</c>) to refine its releases.
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
        HttpRequest http, ClaimsPrincipal user, CompatLibrary library, HistoryService history, CancellationToken ct,
        int page = 1, int pageSize = 20, string sortKey = "date", string sortDirection = "descending")
    {
        var visible = await library.VisibleVersionIdsAsync(user.CompatView(), ct);
        var movieIds = Ints(http.Query["movieIds"]).Concat(Ints(http.Query["movieId"])).Where(visible.Contains).ToList();
        var types = Types(http.Query["eventType"]);

        // Every version the key sees: the journal filter is per target, so one query per page of versions would not do.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var events = await history.ListAsync(new HistoryFilter(), int.MaxValue, ct);
        var rows = events
            .Where(h => h.TargetId is { } target && visible.Contains(target) && (movieIds.Count == 0 || movieIds.Contains(target)))
            .Select(ToResource)
            .Where(r => types.Count == 0 || types.Contains(r.EventType))
            .ToList();
        var ordered = sortDirection.StartsWith("asc", StringComparison.OrdinalIgnoreCase) ? rows.OrderBy(r => r.Date).ThenBy(r => r.Id).ToList() : rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
        return RadarrCompat.Ok(new RadarrPage<RadarrHistoryRecord>(page, pageSize, sortKey, sortDirection, ordered.Count, ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList()));
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

        var types = Types(http.Query["eventType"]);
        var events = await history.ListAsync(new HistoryFilter(VersionId: movieId), 500, ct);
        return RadarrCompat.Ok(events.Select(ToResource).Where(r => types.Count == 0 || types.Contains(r.EventType)).ToList());
    }

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
