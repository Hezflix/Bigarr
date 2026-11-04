using System.Text.Json;
using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Events;
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

    public sealed record RefreshWork(int ServerId, string Path, MediaChange Change, int Attempt);

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

    /// <summary>The folder a dispatched event changed, or null when the event is not about files on disk. Pure, for the tests.</summary>
    public static (string Folder, MediaChange Change)? ChangeOf(ServerEvent serverEvent)
    {
        ArgumentNullException.ThrowIfNull(serverEvent);

        if (!Types.TryGetValue(serverEvent.Type, out var type))
        {
            return null;
        }

        var domainEvent = (DomainEvent?)JsonSerializer.Deserialize(serverEvent.Payload, type, EventJson.Options);
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
                if (ChangeOf(serverEvent) is not { } change)
                {
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var servers = await db.MediaServers.AsNoTracking().Where(s => s.Enabled).Select(s => s.Id).ToListAsync(ct);
                foreach (var serverId in servers)
                {
                    _work.Writer.TryWrite(new RefreshWork(serverId, change.Folder, change.Change, 0));
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
            if (await ProcessAsync(work, ct) || work.Attempt >= RetryDelays.Length)
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
            await clients.For(server.Kind).NotifyAsync(server, translated, work.Change, ct);
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
        catch (Exception ex)
        {
            server.LastError = ex.Message;
            server.LastErrorAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("{Kind} {Server} refused the refresh of {Path} (attempt {Attempt}): {Error}", server.Kind, server.Name, work.Path, work.Attempt + 1, ex.Message);
            return false;
        }
    }
}
