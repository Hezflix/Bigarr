using System.Collections.Concurrent;
using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Import;

/// <summary>
/// Grabs waiting for the import worker. The queue poller and the retry action enqueue; the worker imports one at a
/// time, so a long copy never blocks the poll and two imports never fight over the same folder.
/// </summary>
public sealed class ImportQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, byte> _pending = new();

    public ChannelReader<int> Reader => _channel.Reader;

    /// <summary>False when the grab is already queued or being imported.</summary>
    public bool TryEnqueue(int grabId) => _pending.TryAdd(grabId, 0) && _channel.Writer.TryWrite(grabId);

    public bool IsPending(int grabId) => _pending.ContainsKey(grabId);

    public void Done(int grabId) => _pending.TryRemove(grabId, out _);
}

/// <summary>Drains <see cref="ImportQueue"/>: one completed download at a time goes through <see cref="ImportService"/>.</summary>
public sealed class ImportRunner(ImportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ImportRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var grabId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ImportService>().ImportGrabAsync(grabId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Import of grab {GrabId} crashed", grabId);
                    await ReleaseAsync(grabId, stoppingToken);
                }
                finally
                {
                    queue.Done(grabId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>
    /// An import that crashed outside its own failure handling (the database refused the save that records the outcome)
    /// left its grab in <see cref="GrabState.Importing"/>, a state only this worker leaves. Back to pending: the next
    /// poll queues it again.
    /// </summary>
    private async Task ReleaseAsync(int grabId, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InterruptedImports>().ReleaseAsync(grabId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not put grab {GrabId} back to import pending", grabId);
        }
    }
}

/// <summary>
/// <see cref="GrabState.Importing"/> is held by the import worker while it works on a grab and is never left by anyone
/// else. A process that stops mid-import (restart, kill, crash) therefore strands the grab there: the poll only queues
/// <see cref="GrabState.ImportPending"/>, the retry only takes <see cref="GrabState.ImportFailed"/>, and the target
/// stays "already downloading". At startup no import can be running, so every importing grab is an interrupted one and
/// goes back to pending; the import itself is safe to run again (it never overwrites a destination).
/// </summary>
public sealed class InterruptedImports(BigarrDbContext db, ILogger<InterruptedImports> logger) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var released = await db.Grabs
            .Where(g => g.State == GrabState.Importing)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.State, GrabState.ImportPending), cancellationToken);
        if (released > 0)
        {
            logger.LogWarning("{Count} import(s) were interrupted by the last shutdown; they are pending again", released);
        }
    }

    /// <summary>One grab, and only if it is still importing: a finished or dismissed grab is left alone.</summary>
    public Task<int> ReleaseAsync(int grabId, CancellationToken ct) =>
        db.Grabs
            .Where(g => g.Id == grabId && g.State == GrabState.Importing)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.State, GrabState.ImportPending), ct);
}
