using System.Threading.Channels;

namespace Bigarr.Api.Features.Migration;

/// <summary>One Radarr import at a time, in the background; the progress of the last one stays readable until the next starts.</summary>
public sealed class RadarrImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<RadarrImportRunner> logger) : BackgroundService
{
    private readonly Channel<RadarrImportProgress> _queue = Channel.CreateUnbounded<RadarrImportProgress>();
    private readonly Lock _gate = new();
    private RadarrImportProgress? _current;

    public RadarrImportStatusResponse Status()
    {
        lock (_gate)
        {
            return _current?.ToResponse() ?? RadarrImportProgress.Idle;
        }
    }

    /// <summary>Queues an import, or returns null when one is already queued or running.</summary>
    public RadarrImportStatusResponse? Enqueue(RadarrImportRequest request)
    {
        lock (_gate)
        {
            if (_current is { State: RadarrImportState.Queued or RadarrImportState.Running })
            {
                return null;
            }

            _current = new RadarrImportProgress(request);
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
                var service = scope.ServiceProvider.GetRequiredService<RadarrImportService>();
                await service.ImportAsync(progress, stoppingToken);
                progress.State = RadarrImportState.Completed;
                var done = progress.ToResponse();
                logger.LogInformation(
                    "Radarr import done: {Movies} film(s) added, {Versions} version(s) added, {Updated} updated, {Files} file(s), {Missing} missing, {Skipped} skipped, {Failed} failed",
                    done.MoviesAdded, done.VersionsAdded, done.VersionsUpdated, done.FilesAdded, done.FilesMissing, done.Skipped, done.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = RadarrImportState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Radarr import failed");
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
