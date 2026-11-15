using System.Security.Claims;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Domain.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Sonarr;

/// <summary>
/// <c>POST /command</c>: Seerr sends <c>MissingEpisodeSearch {seriesId}</c> after re-requesting a show (series-wide, not
/// per season) and <c>RefreshMonitoredDownloads</c>; Bazarr sends <c>RescanSeries {seriesId}</c> after each subtitle.
/// Series ids are version ids, filtered by the key's view. A command Bigarr does not run is still accepted (recorded as
/// completed with a message), as other consumers send Sonarr commands nobody here needs; a search through an <c>all</c>
/// key is refused, that key designating no version.
/// </summary>
public static class SonarrCommandEndpoints
{
    public static void MapSonarrCommands(this RouteGroupBuilder group)
    {
        group.MapPost("/command", RunAsync);
        group.MapGet("/command", JsonHttpResult<List<RadarrCommand>> (SonarrCommandLog log, TimeProvider time) => SonarrCompat.Ok(log.Log.Recent(time.GetUtcNow().UtcDateTime)));
        group.MapGet("/command/{id:int}", Results<JsonHttpResult<RadarrCommand>, NotFound> (int id, SonarrCommandLog log, TimeProvider time) =>
            log.Log.Find(id, time.GetUtcNow().UtcDateTime) is { } command ? SonarrCompat.Ok(command) : TypedResults.NotFound());
    }

    private static async Task<Results<JsonHttpResult<RadarrCommand>, JsonHttpResult<List<RadarrValidationFailure>>>> RunAsync(
        SonarrCommandRequest request,
        ClaimsPrincipal user,
        SonarrLibrary library,
        SeriesService series,
        SeriesSearchQueue searches,
        SeriesWantedService wanted,
        QueueState queue,
        LibraryScanRunner scans,
        TaskRunner tasks,
        SonarrCommandLog log,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var view = user.CompatView();
        var now = time.GetUtcNow().UtcDateTime;
        var body = new Dictionary<string, JsonElement>(request.Extra ?? [], StringComparer.Ordinal);
        var seriesIds = (request.SeriesIds ?? []).Concat(request.SeriesId is { } single ? [single] : []).Distinct().ToList();
        if (request.SeriesId is { } seriesId)
        {
            body["seriesId"] = JsonSerializer.SerializeToElement(seriesId);
        }

        if (request.SeriesIds is { Count: > 0 })
        {
            body["seriesIds"] = JsonSerializer.SerializeToElement(request.SeriesIds);
        }

        if (request.SeasonNumber is { } seasonNumber)
        {
            body["seasonNumber"] = JsonSerializer.SerializeToElement(seasonNumber);
        }

        if (request.EpisodeIds is { Count: > 0 })
        {
            body["episodeIds"] = JsonSerializer.SerializeToElement(request.EpisodeIds);
        }

        var key = name.ToLowerInvariant();
        var searching = key is "missingepisodesearch" or "seriessearch" or "seasonsearch" or "episodesearch" or "cutoffunmetepisodesearch";
        if (searching && view.Kind == CompatViewKind.All)
        {
            return RadarrCompat.BadRequest("Name", SonarrSeriesEndpoints.AllViewWriteRefused);
        }

        switch (key)
        {
            case "missingepisodesearch" when seriesIds.Count == 0:
                {
                    // The Wanted page's "search all missing": every version the key sees with missing episodes.
                    var queued = await SearchWantedAsync(WantedFilter.Missing);
                    return SonarrCompat.Created(log.Log.Record("MissingEpisodeSearch", body, "queued", $"{queued.Count} search(es) queued", now, Searched(queued)));
                }

            case "cutoffunmetepisodesearch":
                {
                    var queued = await SearchWantedAsync(WantedFilter.CutoffUnmet);
                    return SonarrCompat.Created(log.Log.Record("CutoffUnmetEpisodeSearch", body, "queued", $"{queued.Count} search(es) queued", now, Searched(queued)));
                }

            case "missingepisodesearch":
            case "seriessearch":
                {
                    // A version search covers its wanted episodes: missing ones and, for a managed version, upgrades.
                    var queued = new List<SeriesSearchRequest>();
                    foreach (var id in seriesIds)
                    {
                        if (await library.FindAsync(view, id, ct) is not null)
                        {
                            var search = new SeriesSearchRequest(id, null, SearchTrigger.User);
                            if (searches.TryEnqueue(search))
                            {
                                queued.Add(search);
                            }
                        }
                    }

                    var display = key == "seriessearch" ? "SeriesSearch" : "MissingEpisodeSearch";
                    return SonarrCompat.Created(log.Log.Record(display, body, "queued", $"{queued.Count} series queued for search", now, Searched(queued)));
                }

            case "seasonsearch":
                {
                    var queued = new List<SeriesSearchRequest>();
                    if (seriesIds.FirstOrDefault() is var id and > 0 && request.SeasonNumber is { } season && await library.FindAsync(view, id, ct) is not null)
                    {
                        var episodes = (await library.EpisodesAsync(id, ct)).Where(t => t.Episode.SeasonNumber == season && t.Monitored).Select(t => t.EpisodeId).ToList();
                        var search = new SeriesSearchRequest(id, episodes, SearchTrigger.User);
                        if (episodes.Count > 0 && searches.TryEnqueue(search))
                        {
                            queued.Add(search);
                        }
                    }

                    return SonarrCompat.Created(log.Log.Record("SeasonSearch", body, "queued", $"{queued.Count} season(s) queued for search", now, Searched(queued)));
                }

            case "episodesearch":
                {
                    var queued = new List<SeriesSearchRequest>();
                    var ids = (request.EpisodeIds ?? []).Distinct().ToList();
                    foreach (var group in (await library.SeriesOfEpisodesAsync(ids, ct)).GroupBy(p => p.Value, p => p.Key))
                    {
                        var show = await library.FindShowAsync(group.Key, ct);
                        if (show is not null && SonarrLibrary.Designated(view, show) is { } version)
                        {
                            var search = new SeriesSearchRequest(version.Id, group.Order().ToList(), SearchTrigger.User);
                            if (searches.TryEnqueue(search))
                            {
                                queued.Add(search);
                            }
                        }
                    }

                    return SonarrCompat.Created(log.Log.Record("EpisodeSearch", body, "queued", $"{queued.Count} search(es) queued", now, Searched(queued)));
                }

            case "rescanseries":
                {
                    // Bazarr asks after each subtitle it writes. Sonarr rescans the series folder; Bigarr rescans the version's.
                    var scanned = new List<int>();
                    foreach (var id in seriesIds)
                    {
                        if (await library.FindAsync(view, id, ct) is { } row)
                        {
                            var versionId = row.Version.Id;
                            scans.EnqueueSeries(row.Series.Id, (services, progress, token) => services.GetRequiredService<SeriesLibraryScanner>().ScanVersionAsync(versionId, progress, token));
                            scanned.Add(row.Series.Id);
                        }
                    }

                    return SonarrCompat.Created(log.Log.Record("RescanSeries", body, "queued", $"{scanned.Count} series queued for a rescan", now, () => scanned.All(id => scans.SeriesStatus(id).State is not (ScanState.Queued or ScanState.Running))));
                }

            case "refreshseries":
                {
                    var refreshed = new HashSet<int>();
                    foreach (var id in seriesIds)
                    {
                        var row = await library.FindAsync(view, id, ct);
                        if (row is not null && refreshed.Add(row.Series.Id))
                        {
                            await series.RefreshAsync(row.Series.Id, ct);
                        }
                    }

                    return SonarrCompat.Created(log.Log.Record("RefreshSeries", body, "completed", $"{refreshed.Count} series refreshed", now));
                }

            case "refreshmonitoreddownloads":
                queue.RequestRefresh();
                return SonarrCompat.Created(log.Log.Record("RefreshMonitoredDownloads", body, "completed", null, now));

            case "rsssync":
                {
                    var sync = Task.Run(() => tasks.RunAsync(RssSyncJob.JobName, CancellationToken.None), CancellationToken.None);
                    return SonarrCompat.Created(log.Log.Record("RssSync", body, "queued", null, now, () => sync.IsCompleted));
                }

            default:
                loggers.CreateLogger("Bigarr.Compat.Sonarr").LogInformation("Sonarr command '{Name}' accepted and not run: Bigarr has no equivalent", name);
                return SonarrCompat.Created(log.Log.Record(name.Length == 0 ? "Unknown" : name, body, "completed", "Not supported by Bigarr: nothing was done.", now));
        }

        async Task<List<SeriesSearchRequest>> SearchWantedAsync(WantedFilter filter)
        {
            var visible = await library.VisibleVersionIdsAsync(view, ct);
            var queued = new List<SeriesSearchRequest>();
            foreach (var group in await wanted.GroupsAsync(null, filter, ct))
            {
                var search = new SeriesSearchRequest(group.VersionId, group.EpisodeIds, SearchTrigger.User);
                if (visible.Contains(group.VersionId) && searches.TryEnqueue(search))
                {
                    queued.Add(search);
                }
            }

            return queued;
        }

        // A search command is over once none of the searches it queued is waiting or being searched any more.
        Func<bool> Searched(List<SeriesSearchRequest> queued) => () => !queued.Any(searches.IsPending);
    }
}

/// <summary>The Sonarr surface's own command log: its ids must not interleave with the Radarr surface's.</summary>
public sealed class SonarrCommandLog
{
    public CompatCommandLog Log { get; } = new();
}
