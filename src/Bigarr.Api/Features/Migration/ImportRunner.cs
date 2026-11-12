using System.Threading.Channels;

namespace Bigarr.Api.Features.Migration;

/// <summary>What a background import exposes to its runner: its lifecycle, and the status the API reads.</summary>
public interface IImportProgress<out TResponse>
{
    RadarrImportState State { get; set; }

    DateTime? StartedAt { get; set; }

    DateTime? FinishedAt { get; set; }

    string? Current { get; set; }

    string? Error { get; set; }

    TResponse ToResponse();
}

/// <summary>
/// One import of a kind at a time, in the background; the progress of the last one stays readable until the next
/// starts. The Radarr, Sonarr and Tautulli imports differ only in what they run and what they log when done.
/// </summary>
public abstract class ImportRunner<TRequest, TProgress, TResponse>(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger logger) : BackgroundService
    where TProgress : class, IImportProgress<TResponse>
    where TResponse : class
{
    private readonly Channel<TProgress> _queue = Channel.CreateUnbounded<TProgress>();
    /// <summary>Guards the current import; a runner that adds its own claims (Tautulli's upload) takes it too.</summary>
    protected Lock Gate { get; } = new();
    private TProgress? _current;

    /// <summary>The source's name in the logs: "Radarr", "Sonarr", "Tautulli".</summary>
    protected abstract string Source { get; }

    /// <summary>The status shown before any import ran.</summary>
    protected abstract TResponse Idle { get; }

    protected abstract TProgress NewProgress(TRequest request);

    /// <summary>Runs the import in its own scope; the progress is written as it goes.</summary>
    protected abstract Task RunAsync(IServiceProvider services, TProgress progress, CancellationToken ct);

    protected abstract void LogDone(ILogger log, TResponse done);

    /// <summary>An extra reason not to start an import, read under <see cref="Gate"/>.</summary>
    protected virtual bool StartHeld => false;

    /// <summary>True while an import is queued or running; read under <see cref="Gate"/>.</summary>
    protected bool HasActiveImport => IsActive(_current);

    public TResponse Status()
    {
        lock (Gate)
        {
            return _current is null ? Idle : _current.ToResponse();
        }
    }

    /// <summary>Queues an import, or returns null when one is already queued or running, or the start is held.</summary>
    public TResponse? Enqueue(TRequest request)
    {
        lock (Gate)
        {
            if (IsActive(_current) || StartHeld)
            {
                return default;
            }

            _current = NewProgress(request);
            _queue.Writer.TryWrite(_current);
            return _current.ToResponse();
        }
    }

    /// <summary>True while an import is queued or running.</summary>
    public bool IsBusy
    {
        get
        {
            lock (Gate)
            {
                return IsActive(_current);
            }
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
                await RunAsync(scope.ServiceProvider, progress, stoppingToken);
                progress.State = RadarrImportState.Completed;
                LogDone(logger, progress.ToResponse());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                progress.State = RadarrImportState.Failed;
                progress.Error = "Shutting down.";
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Source} import failed", Source);
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

    private static bool IsActive(TProgress? progress) => progress is { State: RadarrImportState.Queued or RadarrImportState.Running };
}
