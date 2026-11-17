namespace Bigarr.Api.Features.Migration;

/// <summary>One Seerr import at a time, in the background, never alongside an upload of its file.</summary>
public sealed class SeerrImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<SeerrImportRunner> logger)
    : ImportRunner<SeerrImportRequest, SeerrImportProgress, SeerrImportStatusResponse>(scopeFactory, time, logger)
{
    private bool _uploading;

    protected override string Source => "Seerr";

    protected override SeerrImportStatusResponse Idle => SeerrImportProgress.Idle;

    protected override bool StartHeld => _uploading;

    /// <summary>Claims the uploaded file for one upload: false while an import is queued or running, or another upload is streaming.</summary>
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

    protected override SeerrImportProgress NewProgress(SeerrImportRequest request) => new(request);

    protected override Task RunAsync(IServiceProvider services, SeerrImportProgress progress, CancellationToken ct) =>
        services.GetRequiredService<SeerrImportService>().ImportAsync(progress, ct);

    protected override void LogDone(ILogger log, SeerrImportStatusResponse done) =>
        log.LogInformation(
            "Seerr import done: {Users} user(s) added, {Matched} matched, {Requests} request(s), {Issues} issue(s) and {Comments} comment(s) added, {Already} already imported, {Skipped} skipped",
            done.UsersAdded, done.UsersMatched, done.RequestsAdded, done.IssuesAdded, done.CommentsAdded, done.AlreadyImported, done.Skipped);
}
