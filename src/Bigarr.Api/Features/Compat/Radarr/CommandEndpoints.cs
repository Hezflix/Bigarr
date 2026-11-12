using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary>Recent commands, in memory: Radarr lets clients poll <c>/command/{id}</c>; none of ours does, but the shape is there.</summary>
public sealed class CompatCommandLog
{
    private const int Keep = 100;

    private readonly ConcurrentQueue<RadarrCommand> _recent = new();
    private int _nextId;

    public RadarrCommand Record(string name, Dictionary<string, JsonElement> body, string status, string? message, DateTime now)
    {
        var id = Interlocked.Increment(ref _nextId);
        var command = new RadarrCommand(id, name, name, message, body, "normal", status, status == "completed" ? "successful" : "unknown", now, now, status == "completed" ? now : null, status == "completed" ? "00:00:00" : null, "manual", now, true, true);
        _recent.Enqueue(command);
        while (_recent.Count > Keep && _recent.TryDequeue(out _))
        {
        }

        return command;
    }

    public List<RadarrCommand> Recent() => _recent.OrderByDescending(c => c.Id).ToList();

    public RadarrCommand? Find(int id) => _recent.FirstOrDefault(c => c.Id == id);
}

/// <summary>
/// <c>POST /command</c>: Seerr sends <c>MoviesSearch</c> after an add and <c>RefreshMonitoredDownloads</c>, Bazarr
/// <c>RescanMovie</c> after a subtitle; dashboards and mobile apps send the Wanted page's "search all" commands. Movie ids
/// are version ids and are filtered by the key's view like everything else.
/// </summary>
public static class CommandEndpoints
{
    public static void MapCompatCommands(this RouteGroupBuilder group)
    {
        group.MapPost("/command", RunAsync);
        group.MapGet("/command", JsonHttpResult<List<RadarrCommand>> (CompatCommandLog log) => RadarrCompat.Ok(log.Recent()));
        group.MapGet("/command/{id:int}", Results<JsonHttpResult<RadarrCommand>, NotFound> (int id, CompatCommandLog log) =>
            log.Find(id) is { } command ? RadarrCompat.Ok(command) : TypedResults.NotFound());
    }

    private static async Task<Results<JsonHttpResult<RadarrCommand>, JsonHttpResult<List<RadarrValidationFailure>>>> RunAsync(
        RadarrCommandRequest request,
        ClaimsPrincipal user,
        CompatLibrary library,
        MovieService movies,
        SearchQueue searches,
        WantedService wanted,
        QueueState queue,
        TaskRunner tasks,
        CompatCommandLog log,
        TimeProvider time,
        CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var view = user.CompatView();
        var now = time.GetUtcNow().UtcDateTime;
        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var ids = (request.MovieIds ?? []).Concat(request.MovieId is { } single ? [single] : []).Distinct().ToList();
        if (ids.Count > 0)
        {
            body["movieIds"] = JsonSerializer.SerializeToElement(ids);
        }

        switch (name.ToLowerInvariant())
        {
            case "moviessearch":
            case "moviesearch":
                {
                    var visible = await library.VisibleVersionIdsAsync(view, ct);
                    var queued = 0;
                    foreach (var id in ids.Where(visible.Contains))
                    {
                        if (searches.TryEnqueue(id, SearchTrigger.User))
                        {
                            queued++;
                        }
                    }

                    return RadarrCompat.Created(log.Record("MoviesSearch", body, "queued", $"{queued} version(s) queued for search", now));
                }

            case "missingmoviessearch":
                return RadarrCompat.Created(log.Record("MissingMoviesSearch", body, "queued", await SearchWantedAsync(WantedFilter.Missing), now));

            case "cutoffunmetmoviessearch":
                return RadarrCompat.Created(log.Record("CutoffUnmetMoviesSearch", body, "queued", await SearchWantedAsync(WantedFilter.CutoffUnmet), now));

            case "refreshmovie":
                {
                    var refreshed = new HashSet<int>();
                    foreach (var id in ids)
                    {
                        var row = await library.FindAsync(view, id, ct);
                        if (row is not null && refreshed.Add(row.Movie.Id))
                        {
                            await movies.RefreshAsync(row.Movie.Id, ct);
                        }
                    }

                    return RadarrCompat.Created(log.Record("RefreshMovie", body, "completed", $"{refreshed.Count} film(s) refreshed", now));
                }

            case "rescanmovie":
                // Bazarr asks after writing a subtitle next to the file. Bigarr does not index subtitle files (yet): nothing to rescan.
                return RadarrCompat.Created(log.Record("RescanMovie", body, "completed", null, now));

            case "refreshmonitoreddownloads":
                queue.RequestRefresh();
                return RadarrCompat.Created(log.Record("RefreshMonitoredDownloads", body, "queued", null, now));

            case "rsssync":
                _ = Task.Run(() => tasks.RunAsync(RssSyncJob.JobName, CancellationToken.None), CancellationToken.None);
                return RadarrCompat.Created(log.Record("RssSync", body, "queued", null, now));

            default:
                return RadarrCompat.BadRequest("Name", $"Unknown command '{name}'.");
        }

        // Like Radarr's: every wanted version the key sees, the least recently searched first, with no cap. The runner
        // searches one at a time within each indexer's rate limit, so a long list is a slow trickle, not a burst.
        async Task<string> SearchWantedAsync(WantedFilter filter)
        {
            var visible = await library.VisibleVersionIdsAsync(view, ct);
            var versionIds = await wanted.LeastRecentlySearchedAsync(filter, ct);
            var queued = versionIds.Count(id => visible.Contains(id) && searches.TryEnqueue(id, SearchTrigger.User));
            return $"{queued} version(s) queued for search";
        }
    }
}
