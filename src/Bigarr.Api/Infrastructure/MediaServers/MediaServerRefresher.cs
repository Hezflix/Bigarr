using System.Text.Json;
using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.MediaServers;

/// <summary>
/// Tells every enabled media server about a changed folder (blueprint §3.9: one more consumer of the outbox, like the
/// webhooks and the compat hub). A server that does not answer is retried a few times with growing delays; the
/// outcome is written on the server row so the health check and the settings page can show it.
/// </summary>
public sealed class MediaServerRefresher(EventStream stream, IServiceScopeFactory scopeFactory, MediaServerClients clients, TimeProvider time, ILogger<MediaServerRefresher> logger) : BackgroundService
{
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    private static readonly Dictionary<string, Type> Types = DomainEventCatalog.Entries.ToDictionary(e => e.Type, e => e.Clr, StringComparer.Ordinal);

    private readonly Channel<RefreshWork> _work = Channel.CreateUnbounded<RefreshWork>();

    public sealed record RefreshWork(int ServerId, string Path, MediaChange Change, int Attempt, MediaKind Kind = MediaKind.Movies);

    /// <summary>A folder an event changed, and whether it holds films or a show (which Plex sections to fall back to).</summary>
    public sealed record FolderChange(string Folder, MediaChange Change, MediaKind Kind);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(ListenAsync(stoppingToken), WorkAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>The first folder a dispatched event changed, or null when the event is not about files on disk. Pure, for the tests.</summary>
    public static (string Folder, MediaChange Change)? ChangeOf(ServerEvent serverEvent) =>
        ChangesOf(serverEvent).FirstOrDefault() is { } first ? (first.Folder, first.Change) : null;

    /// <summary>
    /// Every folder a dispatched event changed: a film's folder, or for a show the season folder of each file (a rename
    /// can touch several), the version's folder when it went. Pure, for the tests.
    /// </summary>
    public static List<FolderChange> ChangesOf(ServerEvent serverEvent)
    {
        ArgumentNullException.ThrowIfNull(serverEvent);

        if (!Types.TryGetValue(serverEvent.Type, out var type))
        {
            return [];
        }

        var domainEvent = (DomainEvent?)JsonSerializer.Deserialize(serverEvent.Payload, type, EventJson.Options);
        if (SeriesChanges(domainEvent) is { } series)
        {
            return series.Where(c => !string.IsNullOrEmpty(c.Folder)).DistinctBy(c => (c.Folder, c.Change)).ToList();
        }

        return FilmChange(domainEvent) is { } film ? [new FolderChange(film.Folder, film.Change, MediaKind.Movies)] : [];
    }

    private static List<FolderChange>? SeriesChanges(DomainEvent? domainEvent)
    {
        static FolderChange? In(string filePath, MediaChange change) =>
            Path.GetDirectoryName(filePath) is { Length: > 0 } folder ? new FolderChange(folder, change, MediaKind.Series) : null;

        return domainEvent switch
        {
            EpisodeFileImported e => [In(e.File.Path, e.IsUpgrade ? MediaChange.Modified : MediaChange.Created)!],
            EpisodeFileRenamed e => e.Files.SelectMany(f => new[] { In(f.File.Path, MediaChange.Modified), In(f.PreviousPath, MediaChange.Modified) }).OfType<FolderChange>().ToList(),
            EpisodeFileDeleted e => [In(e.File.Path, MediaChange.Deleted)!],
            SeriesVersionDeleted { DeletedFiles: true } e => e.Version.Folder is { } folder ? [new FolderChange(folder, MediaChange.Deleted, MediaKind.Series)] : [],
            SeriesDeleted { DeletedFiles: true } e => (e.Versions ?? []).Select(v => v.Folder).OfType<string>().Distinct().Select(f => new FolderChange(f, MediaChange.Deleted, MediaKind.Series)).ToList(),
            EpisodeFileImported or EpisodeFileRenamed or EpisodeFileDeleted or SeriesVersionDeleted or SeriesDeleted => [],
            _ => null,
        };
    }

    private static (string Folder, MediaChange Change)? FilmChange(DomainEvent? domainEvent)
    {
        return domainEvent switch
        {
            FileImported e => Folder(e.File.Path, MediaChange.Created),
            FileUpgraded e => Folder(e.File.Path, MediaChange.Modified),
            FileRenamed e => Folder(e.File.Path, MediaChange.Modified),
            FileDeleted e => Folder(e.File.Path, MediaChange.Deleted),
            VersionDeleted { DeletedFiles: true } e => e.Version.Folder is { } folder ? (folder, MediaChange.Deleted) : null,
            MovieDeleted { DeletedFiles: true } e => (e.Versions ?? []).Select(v => v.Folder).FirstOrDefault(f => f is not null) is { } folder ? (folder, MediaChange.Deleted) : null,
            _ => null,
        };
    }

    private static (string, MediaChange)? Folder(string filePath, MediaChange change)
    {
        var folder = Path.GetDirectoryName(filePath);
        return string.IsNullOrEmpty(folder) ? null : (folder, change);
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        await foreach (var serverEvent in stream.SubscribeAsync(ct))
        {
            try
            {
                var changes = ChangesOf(serverEvent);
                if (changes.Count == 0)
                {
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var servers = await db.MediaServers.AsNoTracking().Where(s => s.Enabled).Select(s => s.Id).ToListAsync(ct);
                foreach (var serverId in servers)
                {
                    foreach (var change in changes)
                    {
                        _work.Writer.TryWrite(new RefreshWork(serverId, change.Folder, change.Change, 0, change.Kind));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not queue media server refreshes for {Type} event {Id}", serverEvent.Type, serverEvent.Id);
            }
        }
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        await foreach (var work in _work.Reader.ReadAllAsync(ct))
        {
            bool done;
            try
            {
                done = await ProcessAsync(work, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Nothing may end this loop: it is the only worker, and a faulted one would drop every later refresh in silence.
                logger.LogError(ex, "Refresh of {Path} on media server {Server} crashed (attempt {Attempt})", work.Path, work.ServerId, work.Attempt + 1);
                done = false;
            }

            if (done || work.Attempt >= RetryDelays.Length)
            {
                continue;
            }

            var delay = RetryDelays[work.Attempt];
            _ = RetryLaterAsync(work with { Attempt = work.Attempt + 1 }, delay, ct);
        }
    }

    private async Task RetryLaterAsync(RefreshWork work, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, time, ct);
            _work.Writer.TryWrite(work);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>True when the server took the refresh, or no longer needs it (deleted, disabled).</summary>
    private async Task<bool> ProcessAsync(RefreshWork work, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var server = await db.MediaServers.FirstOrDefaultAsync(s => s.Id == work.ServerId, ct);
        if (server is null || !server.Enabled)
        {
            return true;
        }

        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            var translated = server.Translate(work.Path);
            await clients.For(server.Kind).NotifyAsync(server, translated, work.Change, ct, work.Kind);
            server.LastRefreshAt = now;
            server.LastError = null;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("{Kind} {Server} told about {Change} in {Path}", server.Kind, server.Name, work.Change, translated);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The server was deleted while it was being told: there is no row left to write the outcome on.
            return true;
        }
        catch (Exception ex)
        {
            server.LastError = ex.Message;
            server.LastErrorAt = now;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return true;
            }

            logger.LogWarning("{Kind} {Server} refused the refresh of {Path} (attempt {Attempt}): {Error}", server.Kind, server.Name, work.Path, work.Attempt + 1, ex.Message);
            return false;
        }
    }
}
