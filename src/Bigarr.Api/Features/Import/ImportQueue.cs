using System.Collections.Concurrent;
using System.Threading.Channels;

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
}
