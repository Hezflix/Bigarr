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

/// <summary>
/// Recent commands, in memory: Radarr lets clients poll <c>/command/{id}</c> until it reads <c>completed</c>. A
/// command whose work goes on in the background is recorded <c>queued</c> with a probe that tells when that work is
/// over; reading the command asks the probe, so a client that polls to completion gets there.
/// </summary>
public sealed class CompatCommandLog
{
    private const int Keep = 100;

    private readonly ConcurrentDictionary<int, Entry> _recent = new();
    private int _nextId;

    private sealed record Entry(RadarrCommand Command, Func<bool>? Finished);

    /// <param name="finished">For a <c>queued</c> command: true once its background work is over. Null when the status never changes.</param>
    public RadarrCommand Record(string name, Dictionary<string, JsonElement> body, string status, string? message, DateTime now, Func<bool>? finished = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var command = new RadarrCommand(id, name, name, message, body, "normal", status, status == "completed" ? "successful" : "unknown", now, now, status == "completed" ? now : null, status == "completed" ? "00:00:00" : null, "manual", now, true, true);
        _recent[id] = new Entry(command, status == "completed" ? null : finished);
        foreach (var old in _recent.Keys.Where(k => k <= id - Keep))
        {
            _recent.TryRemove(old, out _);
        }

        return command;
    }

    public List<RadarrCommand> Recent(DateTime now) => _recent.Keys.OrderByDescending(k => k).Select(k => Find(k, now)).OfType<RadarrCommand>().ToList();

    public RadarrCommand? Find(int id, DateTime now)
    {
        if (!_recent.TryGetValue(id, out var entry))
        {
            return null;
        }

        if (entry.Finished is null || !entry.Finished())
        {
            return entry.Command;
        }

        var done = entry.Command with
        {
            Status = "completed",
            Result = "successful",
            Ended = now,
            Duration = (now - entry.Command.Queued).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture),
            StateChangeTime = now,
        };
        _recent[id] = new Entry(done, null);
        return done;
    }
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
        group.MapGet("/command", JsonHttpResult<List<RadarrCommand>> (CompatCommandLog log, TimeProvider time) => RadarrCompat.Ok(log.Recent(time.GetUtcNow().UtcDateTime)));
        group.MapGet("/command/{id:int}", Results<JsonHttpResult<RadarrCommand>, NotFound> (int id, CompatCommandLog log, TimeProvider time) =>
            log.Find(id, time.GetUtcNow().UtcDateTime) is { } command ? RadarrCompat.Ok(command) : TypedResults.NotFound());
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
                    var queued = ids.Where(visible.Contains).Where(id => searches.TryEnqueue(id, SearchTrigger.User)).ToList();
                    return RadarrCompat.Created(log.Record("MoviesSearch", body, "queued", $"{queued.Count} version(s) queued for search", now, Searched(queued)));
                }

            case "missingmoviessearch":
                {
                    var queued = await SearchWantedAsync(WantedFilter.Missing);
                    return RadarrCompat.Created(log.Record("MissingMoviesSearch", body, "queued", $"{queued.Count} version(s) queued for search", now, Searched(queued)));
                }

            case "cutoffunmetmoviessearch":
                {
                    var queued = await SearchWantedAsync(WantedFilter.CutoffUnmet);
                    return RadarrCompat.Created(log.Record("CutoffUnmetMoviesSearch", body, "queued", $"{queued.Count} version(s) queued for search", now, Searched(queued)));
                }

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
                // Asking for a poll is the whole command: it is done once asked.
                queue.RequestRefresh();
                return RadarrCompat.Created(log.Record("RefreshMonitoredDownloads", body, "completed", null, now));

            case "rsssync":
                {
                    var sync = Task.Run(() => tasks.RunAsync(RssSyncJob.JobName, CancellationToken.None), CancellationToken.None);
                    return RadarrCompat.Created(log.Record("RssSync", body, "queued", null, now, () => sync.IsCompleted));
                }

            default:
                return RadarrCompat.BadRequest("Name", $"Unknown command '{name}'.");
        }

        // Like Radarr's: every wanted version the key sees, the least recently searched first, with no cap. The runner
        // searches one at a time within each indexer's rate limit, so a long list is a slow trickle, not a burst.
        async Task<List<int>> SearchWantedAsync(WantedFilter filter)
        {
            var visible = await library.VisibleVersionIdsAsync(view, ct);
            var versionIds = await wanted.LeastRecentlySearchedAsync(filter, ct);
            return versionIds.Where(id => visible.Contains(id) && searches.TryEnqueue(id, SearchTrigger.User)).ToList();
        }

        // A search command is over once none of the versions it queued is waiting or being searched any more.
        Func<bool> Searched(List<int> queued) => () => !queued.Any(searches.IsPending);
    }
}
