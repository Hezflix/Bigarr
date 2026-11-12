using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Media;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Server.Health;

/// <summary>
/// The health checks (blueprint M6): what would stop the loop from running on its own, said in one sentence each.
/// Cheap enough to run every few minutes; nothing here talks to an indexer or a client, it reads what they left.
/// </summary>
public sealed class HealthCheckService(
    BigarrDbContext db,
    IndexerGateway indexers,
    QueueState queue,
    IMediaInfoReader probe,
    IMetadataProvider metadata,
    ISeriesMetadataProvider seriesMetadata,
    Series.SeriesRefreshAlerts refreshAlerts,
    Infrastructure.Xem.XemMappedIds xem,
    IOptions<Infrastructure.Xem.XemOptions> xemOptions,
    MediaManagementService mediaManagement,
    SchedulerState scheduler,
    IOptions<RssSyncOptions> rss,
    IOptions<SchedulerOptions> schedulerOptions,
    IOptions<ShadowOptions> shadow,
    TimeProvider time)
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    public async Task<List<HealthIssue>> CheckAsync(CancellationToken ct)
    {
        var issues = new List<HealthIssue>();
        var now = time.GetUtcNow().UtcDateTime;

        var allIndexers = await db.Indexers.AsNoTracking().ToListAsync(ct);
        if (allIndexers.Count == 0)
        {
            issues.Add(new HealthIssue("indexers", HealthIssueType.Warning, "No indexer is configured: nothing can be searched or grabbed. Add Bigarr to Prowlarr as a Radarr application, or add one in Settings → Indexers."));
        }
        else
        {
            if (!allIndexers.Any(i => i.EnableRss))
            {
                issues.Add(new HealthIssue("indexers", HealthIssueType.Warning, "No indexer has RSS enabled: new releases will not be picked up automatically."));
            }

            if (!allIndexers.Any(i => i.EnableAutomaticSearch))
            {
                issues.Add(new HealthIssue("indexers", HealthIssueType.Warning, "No indexer has automatic search enabled: missing films will not be searched."));
            }

            var down = allIndexers.Select(i => (Indexer: i, Status: indexers.Status(i.Id))).Where(x => !x.Status.Healthy).ToList();
            foreach (var (indexer, status) in down)
            {
                var type = down.Count == allIndexers.Count ? HealthIssueType.Error : HealthIssueType.Warning;
                issues.Add(new HealthIssue("indexerStatus", type, $"Indexer {indexer.Name} is paused until {status.DisabledUntil:HH:mm} UTC after {status.Failures} failure(s): {status.LastError}"));
            }

            if (allIndexers.Any(i => i.Implementation == IndexerImplementation.Mock))
            {
                issues.Add(new HealthIssue("mock", HealthIssueType.Notice, "A mock indexer is configured: it fabricates releases and is meant for trying Bigarr out."));
            }
        }

        var clients = await db.DownloadClients.AsNoTracking().ToListAsync(ct);
        if (shadow.Value.Enabled)
        {
            // A choice, not a fault: no badge, no notification, and the missing client is expected.
            issues.Add(new HealthIssue("shadow", HealthIssueType.Notice, "Shadow mode: Bigarr records what it would grab (Activity → Compare) and sends nothing to a download client. Set Bigarr__Shadow__Enabled=false to grab for real."));
        }
        else if (!clients.Any(c => c.Enabled))
        {
            issues.Add(new HealthIssue("downloadClients", HealthIssueType.Warning, "No download client is enabled: releases cannot be grabbed. Add one in Settings → Download clients."));
        }
        else
        {
            foreach (var status in queue.ClientStatuses().Where(s => !s.Healthy))
            {
                issues.Add(new HealthIssue("downloadClients", HealthIssueType.Error, $"Download client {status.Name} is unreachable: {status.LastError}"));
            }

            if (clients.Any(c => c.Enabled && c.Implementation == DownloadClientImplementation.Mock))
            {
                issues.Add(new HealthIssue("mock", HealthIssueType.Notice, "A mock download client is configured: its downloads complete on their own and produce a two-second clip."));
            }
        }

        foreach (var root in await db.RootFolders.AsNoTracking().ToListAsync(ct))
        {
            if (!Directory.Exists(root.Path))
            {
                issues.Add(new HealthIssue("rootFolders", HealthIssueType.Error, $"Root folder {root.Path} is missing or not mounted."));
            }
        }

        if (rss.Value.Enabled && schedulerOptions.Value.Enabled && allIndexers.Any(i => i.EnableRss))
        {
            var lastRun = scheduler.TryGet(RssSyncJob.JobName, out var runtime) ? runtime.LastRunAt : null;
            var overdue = rss.Value.Interval * 2;
            var reference = lastRun ?? StartedAt;
            if (now - reference > overdue)
            {
                issues.Add(new HealthIssue("rss", HealthIssueType.Warning, lastRun is null
                    ? $"The RSS sync has not run since Bigarr started {(int)(now - reference).TotalMinutes} minutes ago."
                    : $"The RSS sync last ran {(int)(now - reference).TotalMinutes} minutes ago; it should run every {rss.Value.Interval.TotalMinutes:0} minutes."));
            }
        }

        if (!probe.IsAvailable)
        {
            issues.Add(new HealthIssue("mediaInfo", HealthIssueType.Notice, "ffprobe was not found: files import without media info (codecs, channels, HDR). Install ffmpeg to fill it in."));
        }

        var config = await mediaManagement.GetAsync(ct);
        if (config.TransferMode == TransferMode.HardlinkOrCopy)
        {
            var since = now.AddDays(-1);
            var recent = await db.History.AsNoTracking()
                .Where(h => h.CreatedAt >= since && (h.EventType == HistoryEventType.Imported || h.EventType == HistoryEventType.Upgraded))
                .ToListAsync(ct);
            var copied = recent.Count(h => h.Data.TryGetValue("transfer", out var transfer) && string.Equals(transfer, "Copied", StringComparison.OrdinalIgnoreCase) && !h.Data.ContainsKey("manual"));
            if (copied > 0)
            {
                issues.Add(new HealthIssue("imports", HealthIssueType.Warning, $"{copied} import(s) in the last 24 hours had to copy instead of hardlink: downloads and the library are on different filesystems (or different Docker mounts), so seeding files take double the space."));
            }
        }

        if (metadata.Name == "fixtures")
        {
            issues.Add(new HealthIssue("metadata", HealthIssueType.Notice, "No TMDB API key: Bigarr runs on its embedded fixture catalogue of a dozen films. Set Bigarr__Tmdb__ApiKey."));
        }

        // Kept until a run goes through: the scheduler's table remembers the failure across restarts.
        if (scheduler.TryGet(MetadataRefreshJob.JobName, out var refresh) && refresh.LastSucceeded == false)
        {
            issues.Add(new HealthIssue("metadataRefresh", HealthIssueType.Warning, $"The metadata refresh did not finish: {refresh.LastError}"));
        }

        await SeriesChecksAsync(issues, allIndexers, ct);

        var failingServers = await db.MediaServers.AsNoTracking()
            .Where(s => s.Enabled && s.LastErrorAt != null && (s.LastRefreshAt == null || s.LastErrorAt > s.LastRefreshAt))
            .Select(s => new { s.Name, s.Kind, s.LastError })
            .ToListAsync(ct);
        foreach (var server in failingServers)
        {
            issues.Add(new HealthIssue("mediaservers", HealthIssueType.Warning, $"{server.Kind} {server.Name} could not be refreshed: {server.LastError}"));
        }

        var failingEndpoints = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.DeliveredAt == null && (d.NextAttemptAt == null || d.Attempts >= 3))
            .GroupBy(d => d.Endpoint.Name)
            .Select(g => new { Name = g.Key, Count = g.Count(), Error = g.OrderByDescending(d => d.LastAttemptAt).Select(d => d.LastError).FirstOrDefault() })
            .ToListAsync(ct);
        foreach (var endpoint in failingEndpoints)
        {
            issues.Add(new HealthIssue("notifications", HealthIssueType.Warning, $"Webhook {endpoint.Name} is failing ({endpoint.Count} deliver(ies) pending or given up): {endpoint.Error}"));
        }

        return issues.OrderByDescending(i => i.Type).ThenBy(i => i.Source, StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// What the series loop needs (S7): a TVDB key (the fixture catalogue knows three shows), a series refresh that goes
    /// through, TheXEM reachable for scene-numbered shows, indexers that search TV, a root folder for series; and the
    /// releases held for review, counted so they are not forgotten.
    /// </summary>
    private async Task SeriesChecksAsync(List<HealthIssue> issues, List<Indexer> allIndexers, CancellationToken ct)
    {
        if (!await db.Series.AnyAsync(ct))
        {
            return;
        }

        if (seriesMetadata.Name == "fixtures")
        {
            issues.Add(new HealthIssue("seriesMetadata", HealthIssueType.Warning,
                "No TVDB API key: series come from the embedded fixture catalogue of three shows and are never refreshed on their own. Set Bigarr__Tvdb__ApiKey (and Bigarr__Tmdb__ApiKey for images)."));
        }
        else if (!seriesMetadata.CanNumberByTvdb && await db.Series.AnyAsync(s => s.TvdbId != null, ct))
        {
            var count = await db.Series.CountAsync(s => s.TvdbId != null, ct);
            issues.Add(new HealthIssue("seriesMetadata", HealthIssueType.Warning,
                $"TVDB key missing: series episode lists are not refreshed ({count} series with a TVDB id keep the episodes they have; TMDB's numbering never replaces TVDB's). Set Bigarr__Tvdb__ApiKey."));
        }
        else if (refreshAlerts.TvdbUnavailable is { } tvdb)
        {
            issues.Add(new HealthIssue("seriesMetadata", HealthIssueType.Warning,
                $"TVDB failing since {tvdb.At:yyyy-MM-dd HH:mm} UTC: series episode lists are not refreshed until it answers ({tvdb.Reason})."));
        }

        if (refreshAlerts.Refused.Count > 0)
        {
            var refused = refreshAlerts.Refused.Values.Take(3).ToList();
            issues.Add(new HealthIssue("seriesRefresh", HealthIssueType.Warning,
                $"The safety net refused the refresh of {refreshAlerts.Refused.Count} series, their episodes are unchanged: {string.Join(" · ", refused)}{(refreshAlerts.Refused.Count > refused.Count ? " · …" : string.Empty)}"));
        }

        if (scheduler.TryGet(Series.SeriesRefreshJob.JobName, out var refresh) && refresh.LastSucceeded == false)
        {
            issues.Add(new HealthIssue("seriesRefresh", HealthIssueType.Warning, $"The series refresh did not finish (TVDB or TMDB failing): {refresh.LastError}"));
        }

        if (xemOptions.Value.Enabled && xem.LastFailure is { } failure && await db.Series.AnyAsync(s => s.UseSceneNumbering, ct))
        {
            issues.Add(new HealthIssue("xem", HealthIssueType.Warning, $"TheXEM is unreachable since {failure.At:yyyy-MM-dd HH:mm} UTC: scene numbers stay as last read. {failure.Message}"));
        }

        if (!await db.RootFolders.AnyAsync(r => r.Kind == MediaKind.Series, ct))
        {
            issues.Add(new HealthIssue("rootFolders", HealthIssueType.Warning, "No root folder holds series: new series cannot be added. Add one (kind: series) in Settings → Library."));
        }

        // Prowlarr pushes movie categories only: the TV ones come from the capabilities, read on the first series search.
        var searching = allIndexers.Where(i => i.EnableRss || i.EnableAutomaticSearch).ToList();
        if (searching.Count > 0 && searching.All(i => i.TvCategories.Count == 0 && i.AnimeCategories.Count == 0))
        {
            issues.Add(new HealthIssue("indexers", HealthIssueType.Warning, "No indexer has TV categories: series releases cannot be searched or picked from RSS. Bigarr reads them from each indexer's capabilities at the next RSS sync or series search; if this stays, the indexers offer no TV category."));
        }

        var held = await db.HeldReleases.CountAsync(h => h.DismissedAt == null, ct);
        if (held > 0)
        {
            issues.Add(new HealthIssue("heldReleases", HealthIssueType.Notice, $"{held} release(s) held for review: matched on a series' alternative order only. Grab or dismiss them in Activity → Held."));
        }
    }
}

/// <summary>Runs the checks on a schedule, keeps the result and publishes what changed (blueprint §3.9, <c>HealthIssueRaised</c>).</summary>
public sealed class HealthCheckJob(HealthCheckService checks, HealthState state, Infrastructure.Events.OutboxWriter outbox, BigarrDbContext db, TimeProvider time) : IScheduledJob
{
    public const string JobName = "healthCheck";

    public string Name => JobName;

    public string DisplayName => "Health check";

    public TimeSpan Interval => TimeSpan.FromMinutes(10);

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(20);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var issues = await checks.CheckAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;

        // What was standing is read from the database, so a restart neither raises every issue again nor forgets to
        // resolve one fixed while the app was down. The new baseline and its events commit together; the in-memory
        // copy follows only then, so a failed save is simply diffed again by the next check.
        var standing = await db.StandingHealthIssues.ToListAsync(ct);
        foreach (var issue in issues.Where(i => !standing.Any(s => HealthState.Same(s.Source, s.Message, i))))
        {
            db.StandingHealthIssues.Add(new Bigarr.Domain.Scheduling.StandingHealthIssue { Source = issue.Source, Type = issue.Type, Message = issue.Message, RaisedAt = now });
            if (issue.Type != HealthIssueType.Notice)
            {
                outbox.Publish(new HealthIssueRaised(issue.Source, issue.Type, issue.Message) { OccurredAt = now });
            }
        }

        foreach (var gone in standing.Where(s => !issues.Any(i => HealthState.Same(s.Source, s.Message, i))))
        {
            db.StandingHealthIssues.Remove(gone);
            if (gone.Type != HealthIssueType.Notice)
            {
                outbox.Publish(new HealthIssueResolved(gone.Source, gone.Message, gone.Type) { OccurredAt = now });
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        state.Replace(issues, now);

        var errors = issues.Count(i => i.Type == HealthIssueType.Error);
        var warnings = issues.Count(i => i.Type == HealthIssueType.Warning);
        var notices = issues.Count(i => i.Type == HealthIssueType.Notice);
        return issues.Count == 0 ? "All good" : $"{errors} error(s), {warnings} warning(s), {notices} notice(s)";
    }
}
