namespace Bigarr.Api.Features.Migration;

/// <summary>One Tautulli import at a time, in the background, never alongside an upload of its file.</summary>
public sealed class TautulliImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<TautulliImportRunner> logger)
    : ImportRunner<TautulliImportRequest, TautulliImportProgress, TautulliImportStatusResponse>(scopeFactory, time, logger)
{
    private bool _uploading;

    protected override string Source => "Tautulli";

    protected override TautulliImportStatusResponse Idle => TautulliImportProgress.Idle;

    protected override bool StartHeld => _uploading;

    /// <summary>
    /// Claims the uploaded file for one upload, under the same lock as <see cref="ImportRunner{TRequest, TProgress, TResponse}.Enqueue"/>:
    /// false while an import is queued or running, or another upload is streaming. Until <see cref="EndUpload"/> no
    /// import starts, so an upload never replaces the file under an import, and an import never deletes a file a newer
    /// upload just wrote.
    /// </summary>
    public bool TryBeginUpload()
    {
        lock (Gate)
        {
            if (_uploading || HasActiveImport)
            {
                return false;
            }

            _uploading = true;
            return true;
        }
    }

    public void EndUpload()
    {
        lock (Gate)
        {
            _uploading = false;
        }
    }

    protected override TautulliImportProgress NewProgress(TautulliImportRequest request) => new(request);

    protected override Task RunAsync(IServiceProvider services, TautulliImportProgress progress, CancellationToken ct) =>
        services.GetRequiredService<TautulliImportService>().ImportAsync(progress, ct);

    protected override void LogDone(ILogger log, TautulliImportStatusResponse done) =>
        log.LogInformation(
            "Tautulli import done: {Plays} play(s) and {Streams} stream(s) added, {Extended} extended, {Already} already imported, {Overlap} overlapping a live play, {Users} user(s) and {Items} item(s) added",
            done.PlaysAdded, done.StreamsAdded, done.PlaysExtended, done.AlreadyImported, done.OverlappingLive, done.UsersAdded, done.ItemsAdded);
}
