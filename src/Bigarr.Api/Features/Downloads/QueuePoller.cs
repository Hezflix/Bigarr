using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Features.Import;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Downloads;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Events;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Downloads;

/// <summary>
/// One poll of the download clients ("suivi"): every tracked grab moves along its state machine from
/// what its client reports, a grab absent for N polls is <see cref="GrabState.Removed"/>, and the queue projection is
/// rebuilt and published. A finished download waits in <see cref="GrabState.ImportPending"/> until the import worker
/// takes it (<see cref="ImportQueue"/>); a completed torrent of our category that Bigarr never grabbed is adopted so
/// the import can match it by its title. A download the client reports failed, or stalled past the
/// threshold, is blocklisted, removed from the client and replaced by an automatic search (failed handling).
/// </summary>
public sealed class QueuePoller(
    BigarrDbContext db,
    DownloadClientResolver resolver,
    QueueState state,
    ImportQueue imports,
    SearchQueue searches,
    SeriesSearchQueue seriesSearches,
    BlocklistService blocklist,
    OutboxWriter outbox,
    IOptions<DownloadsOptions> options,
    TimeProvider time,
    ILogger<QueuePoller> logger)
{
    public const string FailedMessageTitle = "Download failed";

    private readonly List<FailedRemoval> _removeAfterSave = [];

    /// <summary>A grab the client does not list yet is given this long before absences count.</summary>
    public static readonly TimeSpan FreshGrabGrace = TimeSpan.FromSeconds(45);

    public async Task<QueueResponse> PollAsync(CancellationToken ct)
    {
        await state.PollLock.WaitAsync(ct);
        try
        {
            return await PollCoreAsync(ct);
        }
        finally
        {
            state.PollLock.Release();
        }
    }

    private async Task<QueueResponse> PollCoreAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var settings = options.Value;

        var clients = await db.DownloadClients.AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Priority).ThenBy(c => c.Id).ToListAsync(ct);
        var answered = new Dictionary<int, IReadOnlyList<DownloadItem>>();
        foreach (var client in clients)
        {
            try
            {
                answered[client.Id] = await resolver.For(client).GetItemsAsync(client, ct);
                state.RecordClient(client.Id, client.Name, null, now);
            }
            catch (DownloadClientException ex)
            {
                logger.LogWarning("Download client {Client} did not answer ({Kind}): {Message}", client.Name, ex.Kind, ex.Message);
                state.RecordClient(client.Id, client.Name, ex.Message, now);
            }
        }

        await AdoptCompletedAsync(answered, clientsById: clients.ToDictionary(c => c.Id), now, ct);

        // Every grab still shown: active ones, plus failed ones whose torrent is still in the client.
        var grabs = await db.Grabs.Include(g => g.Targets)
            .Where(g => g.State == GrabState.Grabbed || g.State == GrabState.Downloading || g.State == GrabState.ImportPending
                        || g.State == GrabState.Importing || g.State == GrabState.ImportFailed || g.State == GrabState.DownloadFailed)
            .ToListAsync(ct);
        var versionIds = grabs.SelectMany(g => g.Targets.Select(t => t.TargetId)).Distinct().ToList();
        var versions = await db.MovieVersions.AsNoTracking().Include(v => v.Movie).Include(v => v.QualityProfile).Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var episodeTargets = await db.EpisodeTargets.AsNoTracking()
            .Include(t => t.Episode)
            .Include(t => t.Version).ThenInclude(v => v.Series)
            .Include(t => t.Version).ThenInclude(v => v.RootFolder)
            .Include(t => t.Version).ThenInclude(v => v.QualityProfile)
            .Include(t => t.Version).ThenInclude(v => v.Tags)
            .AsSplitQuery()
            .Where(t => versionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var clientsById = clients.ToDictionary(c => c.Id);

        var rows = new List<QueueItemResponse>();
        var matchedItems = new HashSet<(int ClientId, string DownloadId)>();
        var readyToImport = new List<int>();
        var searchAfterSave = new List<int>();
        var seriesSearchAfterSave = new List<SeriesSearchRequest>();
        _removeAfterSave.Clear();

        foreach (var grab in grabs)
        {
            var version = grab.Targets.Select(t => versions.GetValueOrDefault(t.TargetId)).FirstOrDefault(v => v is not null);
            var episodes = grab.Targets.Select(t => episodeTargets.GetValueOrDefault(t.TargetId)).OfType<Bigarr.Domain.Tv.EpisodeTarget>().ToList();
            var messages = grab.StatusMessages.Select(m => new StatusMessage { Title = m.Title, Messages = [.. m.Messages], Severity = m.Severity }).ToList();
            DownloadItem? item = null;

            if (grab.DownloadClientId is null || !clientsById.TryGetValue(grab.DownloadClientId.Value, out var client))
            {
                if (grab.IsActive)
                {
                    messages.Add(new StatusMessage { Title = "Download client unavailable", Messages = [$"{grab.DownloadClientName ?? "The download client"} is deleted or disabled; the download cannot be followed."], Severity = StatusSeverity.Warning });
                }
            }
            else if (!answered.TryGetValue(client.Id, out var items))
            {
                if (grab.IsActive)
                {
                    messages.Add(new StatusMessage { Title = "Download client unreachable", Messages = [$"{client.Name} did not answer the last poll."], Severity = StatusSeverity.Warning });
                }
            }
            else
            {
                item = items.FirstOrDefault(i => string.Equals(i.DownloadId, grab.DownloadId, StringComparison.OrdinalIgnoreCase));
                if (item is not null)
                {
                    matchedItems.Add((client.Id, item.DownloadId));
                    state.Seen(grab.Id);
                    grab.LastSeenAt = now;
                    var failure = Advance(grab, item, now, settings, messages);
                    if (failure is not null)
                    {
                        var (replace, replaceEpisodes) = await HandleFailureAsync(grab, client, version, episodes, failure, settings, messages, now, ct);
                        if (replace is not null)
                        {
                            searchAfterSave.Add(replace.Value);
                        }

                        if (replaceEpisodes is not null)
                        {
                            seriesSearchAfterSave.Add(replaceEpisodes);
                        }
                    }
                    else if (grab.State == GrabState.ImportPending && item.Status == DownloadItemStatus.Completed)
                    {
                        readyToImport.Add(grab.Id);
                    }
                }
                else if (grab.IsActive)
                {
                    if (grab.State == GrabState.Grabbed && now - grab.GrabbedAt < FreshGrabGrace)
                    {
                        messages.Add(new StatusMessage { Title = "Waiting for the download client", Messages = [$"{client.Name} does not list the torrent yet."], Severity = StatusSeverity.Info });
                    }
                    else
                    {
                        var misses = state.CountMiss(grab.Id);
                        if (misses >= Math.Max(1, settings.RemovedAfterPolls))
                        {
                            logger.LogWarning("{Title} vanished from {Client}: removed by hand, the target is missing again", grab.ReleaseTitle, client.Name);
                            grab.TransitionTo(GrabState.Removed);
                            state.Forget(grab.Id);
                        }
                        else
                        {
                            messages.Add(new StatusMessage { Title = "Not in the download client", Messages = [$"{client.Name} did not list the torrent ({misses}/{settings.RemovedAfterPolls})."], Severity = StatusSeverity.Warning });
                        }
                    }
                }
                else
                {
                    // A failed grab whose torrent is gone: nothing left to show.
                    continue;
                }
            }

            if (grab.State is GrabState.Removed or GrabState.Imported or GrabState.Ignored)
            {
                continue;
            }

            rows.Add(Row(grab, item, version, episodes, clientsById.GetValueOrDefault(grab.DownloadClientId ?? 0), messages, now));
        }

        // Torrents of our category that Bigarr never grabbed (added by hand, or from a wiped database).
        var knownIds = await db.Grabs.AsNoTracking()
            .Where(g => g.DownloadId != null && (g.State == GrabState.Imported || g.State == GrabState.Ignored || g.State == GrabState.Removed))
            .Select(g => g.DownloadId!)
            .ToListAsync(ct);
        var hidden = knownIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (clientId, items) in answered)
        {
            var client = clientsById[clientId];
            foreach (var item in items.Where(i => !matchedItems.Contains((clientId, i.DownloadId)) && !hidden.Contains(i.DownloadId)))
            {
                var series = client.IsSeriesCategory(item.Category);
                rows.Add(new QueueItemResponse(
                    null, item.DownloadId, item.Title, series ? GrabService.EpisodeQualityOf(item.Title) : GrabService.QualityOf(item.Title), null, null, null, null, null, null,
                    item.Status, item.TotalSize, item.RemainingSize, item.Progress, item.DownloadRate, EtaSeconds(item),
                    client.Id, client.Name, null, null, item.OutputPath,
                    [new StatusMessage { Title = "Not grabbed by Bigarr", Messages = [series ? "This torrent was added to the series category by hand. Once complete, the import will match its files to episodes by their names." : "This torrent was added to the category by hand. Once complete, the import will match it to a movie by its title."], Severity = StatusSeverity.Info }],
                    item.AddedAt?.UtcDateTime ?? now, false)
                { Series = series });
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }

        await RemoveFailedAsync(ct);

        foreach (var grabId in readyToImport)
        {
            imports.TryEnqueue(grabId);
        }

        // Only once the failed state is committed: the search checks for an active grab on its own connection.
        foreach (var versionId in searchAfterSave)
        {
            searches.TryEnqueue(versionId, SearchTrigger.FailedDownload);
        }

        foreach (var request in seriesSearchAfterSave)
        {
            seriesSearches.TryEnqueue(request);
        }

        var ordered = rows.OrderByDescending(r => r.Tracked).ThenByDescending(r => r.AddedAt).ToList();
        return state.Publish(new QueueResponse(now, now + settings.PollInterval, (int)settings.PollInterval.TotalSeconds, 0, ordered, state.ClientStatuses()));
    }

    /// <summary>
    /// A completed torrent of our category without a grab row (added by hand, or from a wiped database) gets one, in
    /// import pending: the import then matches it by its title, or fails visibly so the user can import it by hand.
    /// </summary>
    private async Task AdoptCompletedAsync(Dictionary<int, IReadOnlyList<DownloadItem>> answered, Dictionary<int, DownloadClient> clientsById, DateTime now, CancellationToken ct)
    {
        var completed = answered.SelectMany(a => a.Value.Where(i => i.Status == DownloadItemStatus.Completed).Select(i => (ClientId: a.Key, Item: i))).ToList();
        if (completed.Count == 0)
        {
            return;
        }

        var known = (await db.Grabs.AsNoTracking().Where(g => g.DownloadId != null).Select(g => g.DownloadId!).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adopted = new List<Grab>();
        foreach (var (clientId, item) in completed.Where(c => !known.Contains(c.Item.DownloadId)))
        {
            var client = clientsById[clientId];
            var grab = new Grab
            {
                DownloadClientId = client.Id,
                DownloadClientName = client.Name,
                DownloadId = item.DownloadId,
                InfoHash = item.DownloadId,
                ReleaseTitle = item.Title,
                Size = item.TotalSize,
                GrabbedAt = item.AddedAt?.UtcDateTime ?? now,
                LastSeenAt = now,
            };
            grab.TransitionTo(GrabState.ImportPending);
            var what = client.IsSeriesCategory(item.Category) ? "its files to episodes by their names" : "it to a movie by its title";
            grab.StatusMessages.Add(new StatusMessage { Title = "Added by hand", Messages = [$"Found complete in {client.Name} without a grab; the import matches {what}."], Severity = StatusSeverity.Info });
            db.Grabs.Add(grab);
            adopted.Add(grab);
            known.Add(item.DownloadId);
        }

        if (adopted.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var grab in adopted)
            {
                logger.LogInformation("Adopted {Title} from {Client} for import", grab.ReleaseTitle, grab.DownloadClientName);
                imports.TryEnqueue(grab.Id);
            }
        }
    }

    /// <summary>The download state machine, driven by what the client reports. Returns the failure reason when the download just failed.</summary>
    private string? Advance(Grab grab, DownloadItem item, DateTime now, DownloadsOptions settings, List<StatusMessage> messages)
    {
        switch (grab.State)
        {
            case GrabState.Grabbed when item.Status != DownloadItemStatus.Failed:
                grab.TransitionTo(item.Status == DownloadItemStatus.Completed ? GrabState.ImportPending : GrabState.Downloading);
                break;
            case GrabState.Downloading when item.Status == DownloadItemStatus.Completed:
                grab.TransitionTo(GrabState.ImportPending);
                break;
        }

        if (item.Status != DownloadItemStatus.Stalled)
        {
            state.NotStalled(grab.Id);
        }

        if (grab.State is GrabState.Grabbed or GrabState.Downloading)
        {
            if (item.Status == DownloadItemStatus.Failed)
            {
                return item.Message ?? "The download client reports an error.";
            }

            if (item.Status == DownloadItemStatus.Stalled && settings.StalledTimeout is { } timeout && now - state.StalledSince(grab.Id, now) >= timeout)
            {
                return $"Stalled for more than {timeout.TotalMinutes:0} minutes.";
            }
        }

        if (item.Status == DownloadItemStatus.Stalled)
        {
            var since = state.StalledSince(grab.Id, now);
            var detail = item.Message ?? "No peer is sending data.";
            if (settings.StalledTimeout is { } timeout)
            {
                var left = timeout - (now - since);
                detail += $" Treated as failed in {Math.Max(1, Math.Ceiling(left.TotalMinutes)):0} minute(s).";
            }

            messages.Add(new StatusMessage { Title = "Stalled", Messages = [detail], Severity = StatusSeverity.Warning });
        }
        else if (item.Status == DownloadItemStatus.Paused)
        {
            messages.Add(new StatusMessage { Title = "Paused", Messages = ["Stopped in the download client."], Severity = StatusSeverity.Warning });
        }
        else if (item.Message is not null && item.Status != DownloadItemStatus.Completed)
        {
            messages.Add(new StatusMessage { Title = item.Message, Severity = StatusSeverity.Info });
        }

        return null;
    }

    /// <summary>
    /// Failed handling: the grab fails, the release is blocklisted for its version (the default one for a
    /// one-shot; every (version, episode) pair of a series grab), the torrent leaves the client, and the journal and the
    /// outbox get the event. Returns the film version, or the series episodes, to search for a replacement (Sonarr's
    /// RedownloadFailedDownloadService), which the caller queues after the save.
    /// </summary>
    private async Task<(int? Version, SeriesSearchRequest? Episodes)> HandleFailureAsync(Grab grab, DownloadClient client, MovieVersion? version, List<Bigarr.Domain.Tv.EpisodeTarget> episodes, string reason, DownloadsOptions settings, List<StatusMessage> messages, DateTime now, CancellationToken ct)
    {
        grab.TransitionTo(GrabState.DownloadFailed);
        state.Forget(grab.Id);
        grab.StatusMessages.Add(new StatusMessage { Title = FailedMessageTitle, Messages = [reason], Severity = StatusSeverity.Error });
        messages.Add(new StatusMessage { Title = FailedMessageTitle, Messages = [reason], Severity = StatusSeverity.Error });

        var blocked = await blocklist.BlockAsync(grab, $"Download failed: {reason}", now, ct);
        var followUp = new List<string>();
        if (blocked.Count > 0)
        {
            followUp.Add("Release blocklisted.");
        }

        // The torrent only leaves the client once the failure is committed (RemoveFailedAsync): removed first, a save
        // that then failed would leave a downloading grab with no torrent, later counted "removed" without its
        // blocklist entry.
        var removed = settings.RemoveFailedDownloads && grab.DownloadId is not null;
        var removal = removed ? new FailedRemoval(grab, client, $"Removed from {client.Name}.") : null;
        if (removal is not null)
        {
            followUp.Add(removal.Line);
            _removeAfterSave.Add(removal);
        }

        var seriesVersion = episodes.FirstOrDefault()?.Version;
        var replaceEpisodes = episodes.Where(t => t.Monitored).Select(t => t.EpisodeId).Distinct().ToList();
        var searching = settings.SearchAfterFailure && (version is not null
            ? version.Monitored
            : seriesVersion is not null && seriesVersion.Monitored && replaceEpisodes.Count > 0);
        if (searching)
        {
            followUp.Add("Searching for a replacement.");
        }

        if (followUp.Count > 0)
        {
            var note = new StatusMessage { Title = "Failed handling", Messages = followUp, Severity = StatusSeverity.Info };
            grab.StatusMessages.Add(note);
            messages.Add(note);
        }

        foreach (var target in episodes)
        {
            var line = GrabService.SeriesHistory(target.Version, target, HistoryEventType.DownloadFailed, grab.ReleaseTitle, grab.IndexerId, grab.IndexerName, now);
            line.Data["reason"] = reason;
            line.Data["downloadClient"] = grab.DownloadClientName ?? string.Empty;
            line.Data["downloadId"] = grab.DownloadId ?? string.Empty;
            line.Data["blocklisted"] = blocked.Any(b => b.TargetId == target.Id) ? "true" : "false";
            line.Data["removedFromClient"] = removed ? "true" : "false";
            line.Data["searchQueued"] = searching && replaceEpisodes.Contains(target.EpisodeId) ? "true" : "false";
            db.History.Add(line);
            removal?.History.Add(line);
        }

        if (episodes.Count > 0)
        {
            outbox.Publish(new EpisodeDownloadFailed(
                SeriesRef.From(seriesVersion!.Series),
                SeriesVersionRef.From(seriesVersion),
                EpisodeRef.From(episodes.Select(t => t.Episode)),
                new ReleaseRef(grab.ReleaseTitle, GrabService.EpisodeQualityOf(grab.ReleaseTitle), grab.IndexerName, grab.Size, null, null, grab.InfoHash),
                grab.Id,
                reason,
                blocked.Count > 0,
                searching)
            { OccurredAt = now });
            logger.LogWarning("{Title} failed in {Client}: {Reason}. {FollowUp}", grab.ReleaseTitle, client.Name, reason, string.Join(" ", followUp));
            return (null, searching ? new SeriesSearchRequest(seriesVersion.Id, replaceEpisodes, SearchTrigger.FailedDownload) : null);
        }

        var failedLine = new HistoryEvent
        {
            TargetId = version?.Id,
            MovieId = version?.MovieId,
            TmdbId = version?.Movie.TmdbId,
            MovieTitle = version?.Movie.Title,
            VersionName = version?.Name,
            EventType = HistoryEventType.DownloadFailed,
            SourceTitle = grab.ReleaseTitle,
            IndexerId = grab.IndexerId,
            IndexerName = grab.IndexerName,
            Data = new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["downloadClient"] = grab.DownloadClientName ?? string.Empty,
                ["downloadId"] = grab.DownloadId ?? string.Empty,
                ["blocklisted"] = blocked.Count > 0 ? "true" : "false",
                ["removedFromClient"] = removed ? "true" : "false",
                ["searchQueued"] = searching ? "true" : "false",
            },
            CreatedAt = now,
        };
        db.History.Add(failedLine);
        removal?.History.Add(failedLine);

        outbox.Publish(new DownloadFailed(
            version is null ? null : MovieRef.From(version.Movie),
            version is null ? null : VersionRef.From(version),
            new ReleaseRef(grab.ReleaseTitle, GrabService.QualityOf(grab.ReleaseTitle), grab.IndexerName, grab.Size, null, null, grab.InfoHash),
            grab.Id,
            reason,
            blocked.Count > 0,
            searching)
        { OccurredAt = now });

        logger.LogWarning("{Title} failed in {Client}: {Reason}. {FollowUp}", grab.ReleaseTitle, client.Name, reason, string.Join(" ", followUp));
        return (searching ? version!.Id : null, null);
    }

    /// <summary>
    /// The failed torrents leave their client, now that the failure is committed. One the client would not let go
    /// stays there and shows in the queue as failed; the grab's note and its journal line are corrected to say so.
    /// </summary>
    private async Task RemoveFailedAsync(CancellationToken ct)
    {
        var corrected = false;
        foreach (var removal in _removeAfterSave)
        {
            try
            {
                await resolver.For(removal.Client).RemoveAsync(removal.Client, removal.Grab.DownloadId!, deleteFiles: true, ct);
            }
            catch (DownloadClientException ex)
            {
                logger.LogWarning("Could not remove the failed download {Title} from {Client}: {Message}", removal.Grab.ReleaseTitle, removal.Client.Name, ex.Message);
                var instead = $"Could not remove it from {removal.Client.Name}: {ex.Message}";
                removal.Grab.StatusMessages = removal.Grab.StatusMessages
                    .Select(m => new StatusMessage { Title = m.Title, Severity = m.Severity, Messages = m.Messages.Select(line => line == removal.Line ? instead : line).ToList() })
                    .ToList();
                foreach (var line in removal.History)
                {
                    line.Data = new Dictionary<string, string>(line.Data) { ["removedFromClient"] = "false" };
                }

                corrected = true;
            }
        }

        _removeAfterSave.Clear();
        if (corrected)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed record FailedRemoval(Grab Grab, DownloadClient Client, string Line)
    {
        public List<HistoryEvent> History { get; } = [];
    }

    private static QueueItemResponse Row(Grab grab, DownloadItem? item, MovieVersion? version, List<Bigarr.Domain.Tv.EpisodeTarget> episodes, DownloadClient? client, List<StatusMessage> messages, DateTime now)
    {
        var status = item?.Status ?? (grab.State == GrabState.DownloadFailed ? DownloadItemStatus.Failed : DownloadItemStatus.Unknown);
        var seriesVersion = episodes.FirstOrDefault()?.Version;
        var isSeries = seriesVersion is not null || (client?.IsSeriesCategory(item?.Category) ?? false);
        return new QueueItemResponse(
            grab.Id,
            grab.DownloadId ?? string.Empty,
            grab.ReleaseTitle,
            isSeries ? GrabService.EpisodeQualityOf(grab.ReleaseTitle) : GrabService.QualityOf(grab.ReleaseTitle),
            version?.MovieId,
            version?.Movie.Title,
            version?.Movie.Year,
            version?.Id ?? seriesVersion?.Id,
            version?.Name ?? seriesVersion?.Name,
            grab.State,
            status,
            item?.TotalSize ?? grab.Size,
            item?.RemainingSize ?? grab.Size,
            item?.Progress ?? 0,
            item?.DownloadRate ?? 0,
            item is null ? null : EtaSeconds(item),
            client?.Id ?? grab.DownloadClientId ?? 0,
            client?.Name ?? grab.DownloadClientName ?? string.Empty,
            grab.IndexerId,
            grab.IndexerName,
            item?.OutputPath,
            messages,
            grab.GrabbedAt,
            true)
        {
            Series = isSeries,
            SeriesId = seriesVersion?.SeriesId,
            SeriesTitle = seriesVersion?.Series.Title,
            SeriesYear = seriesVersion?.Series.Year,
            Episodes = episodes
                .OrderBy(t => t.Episode.SeasonNumber).ThenBy(t => t.Episode.EpisodeNumber)
                .Select(t => new QueueEpisodeResponse(t.EpisodeId, t.Episode.SeasonNumber, t.Episode.EpisodeNumber, t.Episode.AbsoluteEpisodeNumber, t.Episode.Title, t.EpisodeFileId is not null))
                .ToList(),
        };
    }

    private static int? EtaSeconds(DownloadItem item) => item.Eta is { } eta ? (int)Math.Min(int.MaxValue, eta.TotalSeconds) : null;
}

/// <summary>Polls on the configured interval, or sooner when a slice asks for a refresh (grab, removal, client change).</summary>
public sealed class QueueTrackerRunner(IServiceScopeFactory scopeFactory, QueueState state, IOptions<DownloadsOptions> options, TimeProvider time, ILogger<QueueTrackerRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Queue tracking disabled (Bigarr:Downloads:Enabled = false)");
            return;
        }

        logger.LogInformation("Queue tracking every {Interval} seconds, first poll in {Delay} seconds", settings.PollInterval.TotalSeconds, settings.InitialDelaySeconds);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.InitialDelaySeconds)), time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<QueuePoller>().PollAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Queue poll crashed");
                }

                // Sleep until the next tick, or until someone asks for a refresh.
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                tick.CancelAfter(settings.PollInterval);
                try
                {
                    await state.RefreshRequests.ReadAsync(tick.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // the interval elapsed
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }
}
