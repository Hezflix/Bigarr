namespace Bigarr.Api.Features.Migration;

/// <summary>One Radarr import at a time, in the background.</summary>
public sealed class RadarrImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<RadarrImportRunner> logger)
    : ImportRunner<RadarrImportRequest, RadarrImportProgress, RadarrImportStatusResponse>(scopeFactory, time, logger)
{
    protected override string Source => "Radarr";

    protected override RadarrImportStatusResponse Idle => RadarrImportProgress.Idle;

    protected override RadarrImportProgress NewProgress(RadarrImportRequest request) => new(request);

    protected override Task RunAsync(IServiceProvider services, RadarrImportProgress progress, CancellationToken ct) =>
        services.GetRequiredService<RadarrImportService>().ImportAsync(progress, ct);

    protected override void LogDone(ILogger log, RadarrImportStatusResponse done) =>
        log.LogInformation(
            "Radarr import done: {Movies} film(s) added, {Versions} version(s) added, {Updated} updated, {Files} file(s), {Missing} missing, {Skipped} skipped, {Failed} failed",
            done.MoviesAdded, done.VersionsAdded, done.VersionsUpdated, done.FilesAdded, done.FilesMissing, done.Skipped, done.Failed);
}
