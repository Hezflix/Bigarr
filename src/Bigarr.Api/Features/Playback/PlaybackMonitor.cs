using System.Collections.Concurrent;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>Whether Bigarr hears a server's streams right now, for the Now playing page.</summary>
public sealed record ListenerStatus(bool Connected, string? Error, DateTime Since);

/// <summary>
/// Follows the streams of every enabled Plex server. Per server, a loop:
/// <list type="number">
/// <item>read the list of streams and reconcile (after a restart, a reconnection, or while the event stream is down);</item>
/// <item>listen to the server-sent <c>playing</c> events, reading the list again when one is about an unknown stream
/// or a changed state, and every minute while something plays;</item>
/// <item>on any failure, wait 30 s and start over: while Plex refuses the event stream, that is a poll every 30 s.</item>
/// </list>
/// It only reads from Plex, so it runs in shadow mode too. A server whose URL or token changes is followed anew.
/// </summary>
public sealed class PlaybackMonitor(IServiceScopeFactory scopeFactory, PlexClient plex, TimeProvider time, ILogger<PlaybackMonitor> logger) : BackgroundService
{
    public static readonly TimeSpan ServersCheck = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>A stream that worked and dropped (a proxy timeout, a Plex restart) is opened again this soon, quietly.</summary>
    public static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan ReconcileWhilePlaying = TimeSpan.FromSeconds(60);

    private readonly Dictionary<int, Listener> _listeners = [];

    private readonly ConcurrentDictionary<int, ListenerStatus> _status = new();

    private readonly SemaphoreSlim _wake = new(0, 1);

    private sealed record Listener(string Fingerprint, CancellationTokenSource Stop, Task Run);

    public IReadOnlyDictionary<int, ListenerStatus> Status => _status;

    /// <summary>A server was added, changed or removed: look at the list now rather than at the next check.</summary>
    public void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already awake
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FollowServersAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not list the media servers to follow");
                }

                await _wake.WaitAsync(ServersCheck, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            foreach (var listener in _listeners.Values)
            {
                await listener.Stop.CancelAsync();
            }

            await Task.WhenAll(_listeners.Values.Select(l => l.Run));
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }

    private async Task FollowServersAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var servers = await db.MediaServers.AsNoTracking()
            .Where(s => s.Enabled && s.Kind == MediaServerKind.Plex)
            .Select(s => new { s.Id, s.Url, s.Token })
            .ToListAsync(ct);
        var wanted = servers.ToDictionary(s => s.Id, s => s.Url + "\n" + s.Token);

        foreach (var (id, listener) in _listeners.Where(l => !wanted.TryGetValue(l.Key, out var fingerprint) || fingerprint != l.Value.Fingerprint).ToList())
        {
            await listener.Stop.CancelAsync();
            await listener.Run;
            listener.Stop.Dispose();
            _listeners.Remove(id);
            _status.TryRemove(id, out _);
        }

        await CloseUnfollowedSessionsAsync(scope.ServiceProvider, wanted.Keys, ct);

        foreach (var (id, fingerprint) in wanted.Where(w => !_listeners.ContainsKey(w.Key)))
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _listeners[id] = new Listener(fingerprint, stop, Task.Run(() => FollowAsync(id, stop.Token), CancellationToken.None));
        }
    }

    /// <summary>
    /// An open session only ever closes when its server's listener sees the stream gone. A server that is no longer
    /// followed (disabled, or no longer a Plex) has no listener, so its sessions would stay on Now playing for ever:
    /// they close here, as of the last time they were seen, like streams that vanished from the list. Runs after the
    /// listeners of those servers stopped, so nothing else writes their sessions.
    /// </summary>
    private async Task CloseUnfollowedSessionsAsync(IServiceProvider services, IReadOnlyCollection<int> followed, CancellationToken ct)
    {
        var db = services.GetRequiredService<BigarrDbContext>();
        var orphaned = await db.PlaybackSessions.AsNoTracking()
            .Where(s => !followed.Contains(s.MediaServerId))
            .Select(s => s.MediaServerId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var serverId in orphaned)
        {
            var server = await db.MediaServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serverId, ct);
            if (server is null)
            {
                continue;
            }

            await services.GetRequiredService<PlaybackTracker>().ReconcileAsync(server, [], ct);
            logger.LogInformation("{Server} is no longer followed: its open streams are closed", server.Name);
        }
    }

    private async Task FollowAsync(int serverId, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(1, 1);
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var heard = false;
            var delay = RetryDelay;
            try
            {
                var server = await ServerAsync(serverId, ct);
                if (server is null)
                {
                    return;
                }

                await ReconcileAsync(server, gate, ct);
                using var listening = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var periodic = ReconcileWhilePlayingAsync(server, gate, listening.Token);
                try
                {
                    await foreach (var playing in plex.PlayingEventsAsync(server, listening.Token))
                    {
                        // The first word on the stream, a ping at the latest (~10 s), proves it is up.
                        heard = true;
                        if (failures > 0 || !_status.TryGetValue(serverId, out var status) || !status.Connected)
                        {
                            logger.LogInformation("Following the streams of {Server}", server.Name);
                            failures = 0;
                            _status[serverId] = new ListenerStatus(true, null, time.GetUtcNow().UtcDateTime);
                        }

                        if (playing.IsPing)
                        {
                            continue;
                        }

                        if (await OnEventAsync(server, playing, gate, ct))
                        {
                            await ReconcileAsync(server, gate, ct);
                        }
                    }
                }
                finally
                {
                    await listening.CancelAsync();
                    await periodic;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (heard && ex is MediaServerException)
            {
                // A working stream dropped: open it again right away; the status only turns red if that fails.
                logger.LogDebug("Event stream of media server {Id} dropped, reconnecting: {Error}", serverId, ex.Message);
                delay = ReconnectDelay;
            }
            catch (Exception ex)
            {
                failures++;
                var message = ex is MediaServerException ? ex.Message : $"Unexpected error: {ex.Message}";
                if (failures == 1)
                {
                    logger.LogWarning(ex is MediaServerException ? null : ex, "Lost the streams of media server {Id}: {Error}", serverId, message);
                    _status[serverId] = new ListenerStatus(false, message, time.GetUtcNow().UtcDateTime);
                }
                else
                {
                    logger.LogDebug("Media server {Id} still unavailable: {Error}", serverId, message);
                }
            }

            try
            {
                await Task.Delay(delay, time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReconcileWhilePlayingAsync(MediaServer server, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(ReconcileWhilePlaying, time);
            while (await timer.WaitForNextTickAsync(ct))
            {
                await ReconcileIfPlayingAsync(server, gate, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // the event stream ended
        }
    }

    /// <summary>
    /// One tick of the refresh while something plays. A tick that fails (a slow answer, a 5xx) is only that tick's
    /// loss: ending the refresh on it would leave a stream whose "stopped" event was lost open until the event
    /// connection itself drops, which a server that merely hiccuped never does.
    /// </summary>
    public async Task ReconcileIfPlayingAsync(MediaServer server, SemaphoreSlim gate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        try
        {
            bool playing;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                playing = await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().PlaybackSessions.AnyAsync(s => s.MediaServerId == server.Id, ct);
            }

            if (playing)
            {
                await ReconcileAsync(server, gate, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Periodic reconcile of {Server} failed, trying again at the next tick: {Error}", server.Name, ex.Message);
        }
    }

    private async Task ReconcileAsync(MediaServer server, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var live = await plex.SessionsAsync(server, ct);
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PlaybackTracker>().ReconcileAsync(server, live, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> OnEventAsync(MediaServer server, PlexPlayingEvent playing, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PlaybackTracker>().OnEventAsync(server, playing, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<MediaServer?> ServerAsync(int serverId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().MediaServers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serverId && s.Enabled, ct);
    }
}
