namespace Bigarr.Api.Features.Migration;

/// <summary>One Sonarr import at a time, in the background.</summary>
public sealed class SonarrImportRunner(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<SonarrImportRunner> logger)
    : ImportRunner<SonarrImportRequest, SonarrImportProgress, SonarrImportStatusResponse>(scopeFactory, time, logger)
{
    protected override string Source => "Sonarr";

    protected override SonarrImportStatusResponse Idle => SonarrImportProgress.Idle;

    protected override SonarrImportProgress NewProgress(SonarrImportRequest request) => new(request);

    protected override Task RunAsync(IServiceProvider services, SonarrImportProgress progress, CancellationToken ct) =>
        services.GetRequiredService<SonarrImportService>().ImportAsync(progress, ct);

    protected override void LogDone(ILogger log, SonarrImportStatusResponse done) =>
        log.LogInformation(
            "Sonarr import done: {Series} series added, {Versions} version(s) added, {Updated} updated, {Files} file(s), {Missing} missing, {Skipped} skipped, {Failed} failed",
            done.SeriesAdded, done.VersionsAdded, done.VersionsUpdated, done.FilesAdded, done.FilesMissing, done.Skipped, done.Failed);
}
