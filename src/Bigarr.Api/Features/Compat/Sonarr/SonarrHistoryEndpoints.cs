using System.Linq.Expressions;
using System.Security.Claims;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.History;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Auth;
using Bigarr.Domain.History;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

using Bigarr.Domain.Tv;

/// <summary>
/// <c>/history</c>: Bazarr reads the grabs of an episode (<c>eventType=1&amp;episodeId=</c>) for <c>data.nzbInfoUrl</c>.
/// Series events are journalled per (version, episode) target, so the key's view, <c>seriesId</c> (a version id) and
/// <c>episodeId</c> (the shared episode id) all land on the event's target, and the database counts and pages.
/// </summary>
public static class SonarrHistoryEndpoints
{
    public static void MapSonarrHistory(this RouteGroupBuilder group)
    {
        group.MapGet("/history", PageAsync);
        group.MapGet("/history/series", ForSeriesAsync);
    }

    private static async Task<JsonHttpResult<RadarrPage<SonarrHistoryRecord>>> PageAsync(
        HttpRequest http, ClaimsPrincipal user, HistoryService history, SonarrLibrary library, CancellationToken ct,
        int page = 1, int pageSize = 20, string sortKey = "date", string sortDirection = "descending")
    {
        var seriesIds = SonarrEpisodeEndpoints.Ints(http.Query["seriesId"]).Concat(SonarrEpisodeEndpoints.Ints(http.Query["seriesIds"])).ToList();
        var episodeIds = SonarrEpisodeEndpoints.Ints(http.Query["episodeId"]).Concat(SonarrEpisodeEndpoints.Ints(http.Query["episodeIds"])).ToList();
        var filter = new HistoryFilter(EventTypes: EnumTypes(http.Query["eventType"]), TargetEpisodes: Targets(user.CompatView(), seriesIds, episodeIds));

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var ascending = sortDirection.StartsWith("asc", StringComparison.OrdinalIgnoreCase);
        var result = await history.PageAsync(filter, page, pageSize, ascending, ct);
        var records = await ToResourcesAsync(result.Events, library, ct);
        return SonarrCompat.Ok(new RadarrPage<SonarrHistoryRecord>(page, pageSize, sortKey, sortDirection, result.Total, records));
    }

    /// <summary>Sonarr's unpaged variant: the history of one series (version), newest first, optionally one season.</summary>
    private static async Task<Results<JsonHttpResult<List<SonarrHistoryRecord>>, NotFound>> ForSeriesAsync(
        HttpRequest http, int seriesId, ClaimsPrincipal user, HistoryService history, SonarrLibrary library, CancellationToken ct, int? seasonNumber = null)
    {
        var view = user.CompatView();
        if (await library.FindAsync(view, seriesId, ct) is null)
        {
            return TypedResults.NotFound();
        }

        Expression<Func<EpisodeTarget, bool>> targets = seasonNumber is { } season
            ? t => t.VersionId == seriesId && t.Episode.SeasonNumber == season
            : t => t.VersionId == seriesId;
        var events = await history.ListAsync(new HistoryFilter(EventTypes: EnumTypes(http.Query["eventType"]), TargetEpisodes: targets), 500, ct);
        return SonarrCompat.Ok(await ToResourcesAsync(events, library, ct));
    }

    /// <summary>The targets the request may read: the view's versions, narrowed to the asked series (versions) and episodes.</summary>
    private static Expression<Func<EpisodeTarget, bool>> Targets(CompatView view, List<int> seriesIds, List<int> episodeIds)
    {
        var name = view.VersionName;
        return (view.Kind, seriesIds.Count > 0, episodeIds.Count > 0) switch
        {
            (CompatViewKind.Default, false, false) => t => t.Version.IsDefault,
            (CompatViewKind.Default, true, false) => t => t.Version.IsDefault && seriesIds.Contains(t.VersionId),
            (CompatViewKind.Default, false, true) => t => t.Version.IsDefault && episodeIds.Contains(t.EpisodeId),
            (CompatViewKind.Default, true, true) => t => t.Version.IsDefault && seriesIds.Contains(t.VersionId) && episodeIds.Contains(t.EpisodeId),
            (CompatViewKind.All, false, false) => t => true,
            (CompatViewKind.All, true, false) => t => seriesIds.Contains(t.VersionId),
            (CompatViewKind.All, false, true) => t => episodeIds.Contains(t.EpisodeId),
            (CompatViewKind.All, true, true) => t => seriesIds.Contains(t.VersionId) && episodeIds.Contains(t.EpisodeId),
            (_, false, false) => t => t.Version.Name == name,
            (_, true, false) => t => t.Version.Name == name && seriesIds.Contains(t.VersionId),
            (_, false, true) => t => t.Version.Name == name && episodeIds.Contains(t.EpisodeId),
            _ => t => t.Version.Name == name && seriesIds.Contains(t.VersionId) && episodeIds.Contains(t.EpisodeId),
        };
    }

    /// <summary>The journal's event types behind Sonarr's numbers or names, or null for "no filter". Names nobody maps to match nothing.</summary>
    private static List<HistoryEventType>? EnumTypes(Microsoft.Extensions.Primitives.StringValues values)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values.SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            names.Add(int.TryParse(value, out var number) ? EventTypeName(number) : value);
        }

        return names.Count == 0 ? null : Enum.GetValues<HistoryEventType>().Where(t => names.Contains(EventType(t))).ToList();
    }

    /// <summary>Sonarr's <c>EpisodeHistoryEventType</c> numbers.</summary>
    private static string EventTypeName(int sonarrType) => sonarrType switch
    {
        1 => "grabbed",
        2 => "seriesFolderImported",
        3 => "downloadFolderImported",
        4 => "downloadFailed",
        5 => "episodeFileDeleted",
        6 => "episodeFileRenamed",
        7 => "downloadIgnored",
        _ => "unknown",
    };

    public static string EventType(HistoryEventType type) => type switch
    {
        HistoryEventType.Grabbed => "grabbed",
        HistoryEventType.Imported or HistoryEventType.Upgraded => "downloadFolderImported",
        HistoryEventType.DownloadFailed => "downloadFailed",
        HistoryEventType.FileDeleted => "episodeFileDeleted",
        HistoryEventType.Renamed => "episodeFileRenamed",
        HistoryEventType.Blocklisted => "downloadIgnored",
        _ => "unknown",
    };

    private static async Task<List<SonarrHistoryRecord>> ToResourcesAsync(List<HistoryEvent> events, SonarrLibrary library, CancellationToken ct)
    {
        var targets = await library.TargetsAsync(events.Select(e => e.TargetId).OfType<int>().Distinct().ToList(), ct);
        return events.Select(e =>
        {
            var (versionId, episodeId) = e.TargetId is { } id && targets.TryGetValue(id, out var pair) ? pair : (0, 0);
            var data = new Dictionary<string, string>(e.Data, StringComparer.Ordinal);
            if (e.IndexerName is not null)
            {
                data.TryAdd("indexer", e.IndexerName);
            }

            return new SonarrHistoryRecord(
                e.Id,
                episodeId,
                versionId,
                e.SourceTitle,
                [],
                SonarrQueueEndpoints.SonarrQuality(e.SourceTitle ?? string.Empty),
                [],
                0,
                false,
                e.CreatedAt,
                data.GetValueOrDefault("downloadId"),
                EventType(e.EventType),
                data);
        }).ToList();
    }
}
