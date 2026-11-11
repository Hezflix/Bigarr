using System.Threading.Channels;

namespace Bigarr.Api.Features.Migration;

/// <summary>One Sonarr import at a time, in the background; the progress of the last one stays readable until the next starts.</summary>
public sealed class SonarrImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<SonarrImportRunner> logger) : BackgroundService
{
    private readonly Channel<SonarrImportProgress> _queue = Channel.CreateUnbounded<SonarrImportProgress>();
    private readonly Lock _gate = new();
    private SonarrImportProgress? _current;

    public SonarrImportStatusResponse Status()
    {
        lock (_gate)
        {
            return _current?.ToResponse() ?? SonarrImportProgress.Idle;
        }
    }

    /// <summary>Queues an import, or returns null when one is already queued or running.</summary>
    public SonarrImportStatusResponse? Enqueue(SonarrImportRequest request)
    {
        lock (_gate)
        {
            if (_current is { State: RadarrImportState.Queued or RadarrImportState.Running })
            {
                return null;
            }

            _current = new SonarrImportProgress(request);
            _queue.Writer.TryWrite(_current);
            return _current.ToResponse();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var progress in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            progress.State = RadarrImportState.Running;
            progress.StartedAt = time.GetUtcNow().UtcDateTime;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<SonarrImportService>();
                await service.ImportAsync(progress, stoppingToken);
                progress.State = RadarrImportState.Completed;
                var done = progress.ToResponse();
                logger.LogInformation(
                    "Sonarr import done: {Series} series added, {Versions} version(s) added, {Updated} updated, {Files} file(s), {Missing} missing, {Skipped} skipped, {Failed} failed",
                    done.SeriesAdded, done.VersionsAdded, done.VersionsUpdated, done.FilesAdded, done.FilesMissing, done.Skipped, done.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = RadarrImportState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sonarr import failed");
                progress.State = RadarrImportState.Failed;
                progress.Error = ex.Message;
            }
            finally
            {
                progress.FinishedAt = time.GetUtcNow().UtcDateTime;
                progress.Current = null;
            }
        }
    }
}
