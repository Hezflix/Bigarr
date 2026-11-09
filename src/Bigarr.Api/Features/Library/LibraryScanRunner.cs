using System.Collections.Concurrent;
using System.Threading.Channels;
using Bigarr.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

/// <summary>
/// One scan at a time, in the background; progress is kept in memory and served by the API. A job is a whole root
/// folder (scanned by the scanner of its kind) or a single series; each key is queued at most once.
/// </summary>
public sealed class LibraryScanRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<LibraryScanRunner> logger) : BackgroundService
{
    /// <summary>A root folder scan (<see cref="SeriesId"/> 0) or a single series rescan (<see cref="RootFolderId"/> 0).</summary>
    private readonly record struct ScanKey(int RootFolderId, int SeriesId);

    private sealed record ScanJob(ScanKey Key, Func<IServiceProvider, ScanProgress, CancellationToken, Task> Work);

    private readonly Channel<ScanJob> _queue = Channel.CreateUnbounded<ScanJob>();
    private readonly ConcurrentDictionary<ScanKey, ScanProgress> _progress = new();

    public ScanStatusResponse Status(int rootFolderId) => StatusOf(new ScanKey(rootFolderId, 0), () => new ScanProgress(rootFolderId));

    public ScanStatusResponse SeriesStatus(int seriesId) => StatusOf(new ScanKey(0, seriesId), () => new ScanProgress(0) { SeriesId = seriesId });

    /// <summary>Queues a scan unless one is already queued or running for this root folder.</summary>
    public ScanStatusResponse Enqueue(int rootFolderId) =>
        Enqueue(new ScanJob(new ScanKey(rootFolderId, 0), (sp, progress, ct) => ScanRootAsync(sp, rootFolderId, progress, ct)), () => new ScanProgress(rootFolderId));

    /// <summary>Queues a rescan of one series (every version folder); <paramref name="work"/> does it in a fresh scope.</summary>
    public ScanStatusResponse EnqueueSeries(int seriesId, Func<IServiceProvider, ScanProgress, CancellationToken, Task> work) =>
        Enqueue(new ScanJob(new ScanKey(0, seriesId), work), () => new ScanProgress(0) { SeriesId = seriesId });

    public void Forget(int rootFolderId) => _progress.TryRemove(new ScanKey(rootFolderId, 0), out _);

    private ScanStatusResponse StatusOf(ScanKey key, Func<ScanProgress> idle) =>
        _progress.TryGetValue(key, out var progress) ? progress.ToResponse() : WithState(idle(), ScanState.Idle).ToResponse();

    private static ScanProgress WithState(ScanProgress progress, ScanState state)
    {
        progress.State = state;
        return progress;
    }

    private ScanStatusResponse Enqueue(ScanJob job, Func<ScanProgress> fresh)
    {
        var progress = _progress.AddOrUpdate(
            job.Key,
            _ => fresh(),
            (_, current) => current.State is ScanState.Queued or ScanState.Running ? current : fresh());

        if (progress.State == ScanState.Queued && progress.StartedAt is null)
        {
            _queue.Writer.TryWrite(job);
        }

        return progress.ToResponse();
    }

    private static async Task ScanRootAsync(IServiceProvider services, int rootFolderId, ScanProgress progress, CancellationToken ct)
    {
        var db = services.GetRequiredService<BigarrDbContext>();
        var kind = await db.RootFolders.Where(r => r.Id == rootFolderId).Select(r => (Domain.Library.MediaKind?)r.Kind).FirstOrDefaultAsync(ct)
                   ?? throw new InvalidOperationException($"Root folder {rootFolderId} no longer exists.");
        var scanner = services.GetServices<IRootFolderScanner>().FirstOrDefault(s => s.Kind == kind)
                      ?? throw new InvalidOperationException($"No scanner for {kind} root folders.");
        await scanner.ScanAsync(rootFolderId, progress, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_progress.TryGetValue(job.Key, out var progress) || progress.State != ScanState.Queued)
            {
                continue;
            }

            progress.State = ScanState.Running;
            progress.StartedAt = time.GetUtcNow().UtcDateTime;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await job.Work(scope.ServiceProvider, progress, stoppingToken);
                progress.State = ScanState.Completed;
                logger.LogInformation(
                    "Scan {Target} done: {Movies} movie(s), {Series} series, {Versions} version(s), {Files} file(s), {Unmapped} unmapped folder(s), {Unmatched} unmatched file(s)",
                    job.Key.SeriesId > 0 ? $"of series {job.Key.SeriesId}" : $"of root folder {job.Key.RootFolderId}",
                    progress.MoviesAdded, progress.SeriesAdded, progress.VersionsAdded, progress.FilesAdded, progress.Unmapped, progress.UnmatchedFiles);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = ScanState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan {Key} failed", job.Key);
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
