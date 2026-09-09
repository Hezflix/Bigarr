using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Bigarr.Api.Features.Library;

/// <summary>One scan at a time, in the background; progress is kept in memory and served by the API.</summary>
public sealed class LibraryScanRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<LibraryScanRunner> logger) : BackgroundService
{
    private readonly Channel<int> _queue = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, ScanProgress> _progress = new();

    public ScanStatusResponse Status(int rootFolderId) =>
        _progress.TryGetValue(rootFolderId, out var progress) ? progress.ToResponse() : new ScanProgress(rootFolderId) { State = ScanState.Idle }.ToResponse();

    /// <summary>Queues a scan unless one is already queued or running for this root folder.</summary>
    public ScanStatusResponse Enqueue(int rootFolderId)
    {
        var progress = _progress.AddOrUpdate(
            rootFolderId,
            _ => new ScanProgress(rootFolderId),
            (_, current) => current.State is ScanState.Queued or ScanState.Running ? current : new ScanProgress(rootFolderId));

        if (progress.State == ScanState.Queued && progress.StartedAt is null)
        {
            _queue.Writer.TryWrite(rootFolderId);
        }

        return progress.ToResponse();
    }

    public void Forget(int rootFolderId) => _progress.TryRemove(rootFolderId, out _);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var rootFolderId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_progress.TryGetValue(rootFolderId, out var progress) || progress.State != ScanState.Queued)
            {
                continue;
            }

            progress.State = ScanState.Running;
            progress.StartedAt = time.GetUtcNow().UtcDateTime;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<LibraryScanner>();
                await scanner.ScanAsync(rootFolderId, progress, stoppingToken);
                progress.State = ScanState.Completed;
                logger.LogInformation(
                    "Scan of root folder {RootFolderId} done: {Movies} movie(s), {Versions} version(s), {Files} file(s), {Unmapped} unmapped",
                    rootFolderId, progress.MoviesAdded, progress.VersionsAdded, progress.FilesAdded, progress.Unmapped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = ScanState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan of root folder {RootFolderId} failed", rootFolderId);
                progress.State = ScanState.Failed;
                progress.Error = ex.Message;
            }
            finally
            {
                progress.FinishedAt = time.GetUtcNow().UtcDateTime;
                progress.CurrentFolder = null;
            }
        }
    }
}
