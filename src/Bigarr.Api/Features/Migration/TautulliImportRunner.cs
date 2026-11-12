using System.Threading.Channels;

namespace Bigarr.Api.Features.Migration;

/// <summary>One Tautulli import at a time, in the background; the progress of the last one stays readable until the next starts.</summary>
public sealed class TautulliImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<TautulliImportRunner> logger) : BackgroundService
{
    private readonly Channel<TautulliImportProgress> _queue = Channel.CreateUnbounded<TautulliImportProgress>();
    private readonly Lock _gate = new();
    private TautulliImportProgress? _current;
    private bool _uploading;

    public TautulliImportStatusResponse Status()
    {
        lock (_gate)
        {
            return _current?.ToResponse() ?? TautulliImportProgress.Idle;
        }
    }

    /// <summary>Queues an import, or returns null when one is already queued or running.</summary>
    public TautulliImportStatusResponse? Enqueue(TautulliImportRequest request)
    {
        lock (_gate)
        {
            if (_uploading || _current is { State: RadarrImportState.Queued or RadarrImportState.Running })
            {
                return null;
            }

            _current = new TautulliImportProgress(request);
            _queue.Writer.TryWrite(_current);
            return _current.ToResponse();
        }
    }

    /// <summary>True while an import is queued or running: its file must not be replaced under it.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _current is { State: RadarrImportState.Queued or RadarrImportState.Running };
            }
        }
    }

    /// <summary>
    /// Claims the uploaded file for one upload, under the same lock as <see cref="Enqueue"/>: false while an import is
    /// queued or running, or another upload is streaming. Until <see cref="EndUpload"/> no import starts, so an upload
    /// never replaces the file under an import, and an import never deletes a file a newer upload just wrote.
    /// </summary>
    public bool TryBeginUpload()
    {
        lock (_gate)
        {
            if (_uploading || _current is { State: RadarrImportState.Queued or RadarrImportState.Running })
            {
                return false;
            }

            _uploading = true;
            return true;
        }
    }

    public void EndUpload()
    {
        lock (_gate)
        {
            _uploading = false;
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
                var service = scope.ServiceProvider.GetRequiredService<TautulliImportService>();
                await service.ImportAsync(progress, stoppingToken);
                progress.State = RadarrImportState.Completed;
                var done = progress.ToResponse();
                logger.LogInformation(
                    "Tautulli import done: {Plays} play(s) and {Streams} stream(s) added, {Extended} extended, {Already} already imported, {Overlap} overlapping a live play, {Users} user(s) and {Items} item(s) added",
                    done.PlaysAdded, done.StreamsAdded, done.PlaysExtended, done.AlreadyImported, done.OverlappingLive, done.UsersAdded, done.ItemsAdded);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = RadarrImportState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tautulli import failed");
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
