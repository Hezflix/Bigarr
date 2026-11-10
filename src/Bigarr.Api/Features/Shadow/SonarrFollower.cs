using System.Globalization;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.SonarrImport;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>The outcome of one pass over one followed Sonarr.</summary>
public sealed record SonarrFollowPass(int FollowedSonarrId, string Name, bool Success, string Summary, int Grabs, int Resolved, int PicksShown, int SearchedOnAdd = 0);

/// <summary>
/// One pass over a followed Sonarr (shadow mode), the series twin of <see cref="RadarrFollower"/>, reading only, in the
/// same order so that Bigarr judges what Sonarr did against the library as it was before Sonarr's download:
/// <list type="number">
/// <item>the Bigarr profiles its series map to take its profiles' qualities, cutoff and scores (unless a followed Radarr
/// owns them);</item>
/// <item>series Sonarr has and Bigarr does not come in, with their episodes and monitoring but without their files;</item>
/// <item>Sonarr's grabs since the checkpoint become <see cref="SonarrGrab"/> rows, the per-episode history lines of one
/// grab (a season pack writes one per episode) gathered into one row with every episode it covers, judged by Bigarr's
/// engine on the spot; imports and failures close them;</item>
/// <item>a series added to Sonarr since the follow started gets Bigarr's own search on add (the whole-series rule:
/// one request by ids, then season by season), recorded as picks;</item>
/// <item>the rest of the library follows Sonarr's through the import's rules (<see cref="SonarrImportService.FollowAsync"/>):
/// episode files, monitoring and settings, history;</item>
/// <item>Bigarr's latest series picks are shown to Sonarr's <c>/parse</c>.</item>
/// </list>
/// The checkpoint only moves when the whole pass went through. Passes run on the schedule, on demand, and when Sonarr's
/// webhook calls (<see cref="SonarrFollowWakeups"/>).
/// </summary>
public sealed class SonarrFollower(
    BigarrDbContext db,
    SonarrClient sonarr,
    SonarrImportService imports,
    SonarrFollowMemory memory,
    SeriesShadowJudge judge,
    SeriesAutomaticSearchService search,
    IOptions<ShadowOptions> options,
    TimeProvider time,
    ILogger<SonarrFollower> logger)
{
    /// <summary>Each pass reads from this long before the checkpoint: a line written late, with an earlier date, is not missed.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

    /// <summary>Sonarr's own RSS default: a pass every quarter of an hour, and within seconds when its webhook calls.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often a pass reads Sonarr's whole series list: what is not in its history (a series added or deleted, a
    /// monitored flag, a profile, a folder, files counted by its statistics) is caught within this time, or at once when
    /// Sonarr's webhook says so. Between two, a pass reads only the series its history names.
    /// </summary>
    public static readonly TimeSpan FullListInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// How often a pass reads the episodes of every series, not only of those that changed: an episode's monitored flag is
    /// in no history and no statistic. "Sync now" does it too.
    /// </summary>
    public static readonly TimeSpan EverySeriesInterval = TimeSpan.FromHours(24);

    /// <summary>History naming more series than this is cheaper as one list than as one request per series.</summary>
    public const int SeriesFetchedOneByOneMax = 50;

    /// <summary>A Sonarr list importing hundreds of shows at once must not turn into hundreds of searches on the trackers.</summary>
    public const int SearchesOnAddPerPass = 25;

    /// <summary>Bigarr's newest series picks shown to Sonarr's <c>/parse</c> per pass.</summary>
    public const int PicksShownPerPass = 50;

    /// <summary>A manual pass and the scheduled one never run at the same time.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<List<SonarrFollowPass>> RunAllAsync(CancellationToken ct)
    {
        var ids = await db.FollowedSonarrs.AsNoTracking().Where(f => f.Enabled).OrderBy(f => f.Id).Select(f => f.Id).ToListAsync(ct);
        var passes = new List<SonarrFollowPass>();
        foreach (var id in ids)
        {
            passes.Add(await SyncAsync(id, ct));
        }

        return passes;
    }

    /// <param name="fullList">Read the whole series list (a webhook about a series added or deleted).</param>
    /// <param name="everySeries">Also read the episodes of every series ("Sync now").</param>
    public async Task<SonarrFollowPass> SyncAsync(int id, CancellationToken ct, bool fullList = false, bool everySeries = false)
    {
        await Gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var follow = await db.FollowedSonarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
                ?? throw new InvalidOperationException($"Followed Sonarr {id} does not exist.");
            try
            {
                return await SyncCoreAsync(follow, fullList || everySeries, everySeries, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                // A pass that failed may have missed a series: the next one reads the whole list.
                memory.Forget(id);
                var now = time.GetUtcNow().UtcDateTime;
                var message = ex is SonarrUnavailableException ? ex.Message : $"The pass failed: {ex.Message}";
                if (ex is not SonarrUnavailableException)
                {
                    logger.LogError(ex, "Following {Name} failed", follow.Name);
                }
                else
                {
                    logger.LogWarning("Following {Name}: {Message}", follow.Name, ex.Message);
                }

                await db.FollowedSonarrs.Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.LastError, Truncate(message, 2048))
                    .SetProperty(f => f.LastErrorAt, now), ct);
                return new SonarrFollowPass(id, follow.Name, false, message, 0, 0, 0);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<SonarrFollowPass> SyncCoreAsync(FollowedSonarr follow, bool fullList, bool everySeriesAsked, CancellationToken ct)
    {
        var connection = new SonarrConnection(follow.Url, follow.ApiKey);
        var history = await sonarr.HistorySinceAsync(connection, follow.HistoryCheckpoint - Overlap, ct);
        var started = time.GetUtcNow().UtcDateTime;
        var remembered = memory.Get(follow.Id);
        var historySeries = history.Select(h => h.SeriesId).Where(id => id > 0).Distinct().ToList();
        var wholeList = fullList || remembered is null || started - remembered.ListReadAt >= FullListInterval || historySeries.Count > SeriesFetchedOneByOneMax;
        var series = wholeList ? await sonarr.SeriesAsync(connection, ct) : await SeriesAsync(connection, historySeries, ct);
        var seriesPerProfile = wholeList ? SonarrImportService.SeriesPerProfile(series) : remembered!.SeriesPerProfile;
        var everySeries = everySeriesAsked || (remembered is not null && started - remembered.EverySeriesReadAt >= EverySeriesInterval);
        var profiles = await sonarr.ProfilesAsync(connection, ct);
        var tags = await sonarr.TagsAsync(connection, ct);
        // Sonarr blocklists on a failed download: the blocklist is only worth reading when one happened.
        var blocklist = history.Any(h => SonarrImportService.HistoryType(h.EventType) == HistoryEventType.DownloadFailed) ? await sonarr.BlocklistAsync(connection, ct) : [];
        var byId = series.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var data = new SonarrFollowData(profiles, tags, series, history, blocklist);
        var request = new SonarrImportRequest(
            follow.Url,
            follow.ApiKey,
            follow.VersionName,
            follow.ProfileMap,
            follow.RootFolderMap,
            follow.ImportTags,
            ImportExclusions: false,
            ImportHistory: true,
            ImportBlocklist: true,
            follow.KeepMonitored);

        // 0. Sonarr's profile settings, so Bigarr judges everything below the way Sonarr would.
        var aligned = await imports.ApplyProfilesAsync(request, profiles, seriesPerProfile, null, ct);
        var sizes = await imports.SyncSizesAsync(connection, null, ct);

        // 1. New series, without their files: what Sonarr grabbed for them is judged against a show that had none.
        var addedSinceFollowing = await AddedSinceFollowingAsync(follow, series, ct);
        var added = await imports.AddNewSeriesAsync(request, data, ct);
        db.ChangeTracker.Clear();

        // 2. Sonarr's grabs, judged before the follow brings in the files they led to.
        var grabs = await RecordGrabsAsync(follow, connection, history, byId, ct);
        var resolved = await ResolveAsync(follow, history, ct);

        // 3. Bigarr's own search on add for the series Sonarr was given since the follow started.
        var searched = await SearchOnAddAsync(follow, addedSinceFollowing, ct);

        // 4. Files, settings and history.
        var library = await imports.FollowAsync(request, data, everySeries, ct);
        db.ChangeTracker.Clear();

        await LinkAsync(follow, ct);
        var shown = await ShowPicksAsync(follow, connection, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var checkpoint = history.Count > 0 ? new[] { follow.HistoryCheckpoint, history.Max(h => Utc(h.Date)) }.Max() : follow.HistoryCheckpoint;
        var summary = Summary(grabs, resolved, added, library, searched, shown, aligned.Count, everySeries, sizes);
        memory.Remember(follow.Id, new SonarrFollowMemory.Entry(
            wholeList ? started : remembered!.ListReadAt,
            seriesPerProfile,
            everySeries || remembered is null ? started : remembered.EverySeriesReadAt));
        await db.FollowedSonarrs.Where(f => f.Id == follow.Id).ExecuteUpdateAsync(s => s
            .SetProperty(f => f.HistoryCheckpoint, checkpoint)
            .SetProperty(f => f.LastSyncAt, now)
            .SetProperty(f => f.LastSummary, Truncate(summary, 1024))
            .SetProperty(f => f.LastError, (string?)null)
            .SetProperty(f => f.LastErrorAt, (DateTime?)null), ct);

        logger.LogInformation("Following {Name}: {Summary}", follow.Name, summary);
        return new SonarrFollowPass(follow.Id, follow.Name, true, summary, grabs, resolved, shown, searched);
    }

    /// <summary>The series a pass between two full lists looks at: those its history names, one request each; one Sonarr no longer has is skipped.</summary>
    private async Task<List<SonarrSeries>> SeriesAsync(SonarrConnection connection, List<int> ids, CancellationToken ct)
    {
        var found = new List<SonarrSeries>();
        foreach (var id in ids)
        {
            if (await sonarr.SeriesAsync(connection, id, ct) is { } one)
            {
                found.Add(one);
            }
        }

        return found;
    }

    /// <summary>
    /// TVDB ids of the series Sonarr was given since the follow started (its <c>added</c> date, a few minutes of margin)
    /// that Bigarr does not have yet as this version: those get Bigarr's search on add. Never a back catalogue.
    /// </summary>
    private async Task<List<int>> AddedSinceFollowingAsync(FollowedSonarr follow, List<SonarrSeries> series, CancellationToken ct)
    {
        var since = follow.HistoryCheckpoint - Overlap;
        var recent = series.Where(s => s.TvdbId > 0 && s.Monitored && s.Added is { } added && Utc(added) >= since).Select(s => s.TvdbId).Distinct().ToList();
        if (recent.Count == 0)
        {
            return [];
        }

        var known = await db.SeriesVersions.AsNoTracking().Where(v => v.Name == follow.VersionName && v.Series.TvdbId != null && recent.Contains(v.Series.TvdbId.Value)).Select(v => v.Series.TvdbId!.Value).ToListAsync(ct);
        return recent.Except(known).ToList();
    }

    /// <summary>
    /// The counterpart of the search Sonarr runs when Seerr or a user adds a show: Bigarr's automatic series search (one
    /// request by the show's ids, then season by season), in shadow mode only, so the outcome is picks and nothing is sent.
    /// Bounded per pass.
    /// </summary>
    private async Task<int> SearchOnAddAsync(FollowedSonarr follow, List<int> tvdbIds, CancellationToken ct)
    {
        if (tvdbIds.Count == 0 || !options.Value.Enabled)
        {
            return 0;
        }

        var versionIds = await db.SeriesVersions.AsNoTracking()
            .Where(v => v.Name == follow.VersionName && v.Monitored && v.Series.TvdbId != null && tvdbIds.Contains(v.Series.TvdbId.Value))
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(ct);
        if (versionIds.Count > SearchesOnAddPerPass)
        {
            logger.LogWarning("Following {Name}: {Count} series added to Sonarr, Bigarr searches the first {Bound}", follow.Name, versionIds.Count, SearchesOnAddPerPass);
        }

        var results = await search.SearchAsync(versionIds.Take(SearchesOnAddPerPass).ToList(), SearchTrigger.Added, ct);
        db.ChangeTracker.Clear();
        return results.Count;
    }

    private static string Summary(int grabs, int resolved, SonarrImportProgress added, SonarrImportProgress library, int searched, int shown, int profilesAligned, bool everySeries, int sizes = 0)
    {
        var first = added.ToResponse();
        var report = library.ToResponse();
        var parts = new List<string> { $"{grabs} new grab(s)" };
        void Add(int count, string what)
        {
            if (count > 0)
            {
                parts.Add($"{count} {what}");
            }
        }

        Add(profilesAligned, "profile(s) aligned with Sonarr");
        Add(sizes, "series quality size(s) aligned with Sonarr");
        Add(resolved, "grab(s) imported or failed");
        Add(first.SeriesAdded + report.SeriesAdded, "series added");
        Add(first.VersionsAdded + report.VersionsAdded, "version(s) added");
        Add(first.EpisodesAdded + report.EpisodesAdded, "episode(s) added");
        Add(searched, "series searched on add");
        Add(report.VersionsUpdated, "version(s) updated");
        Add(report.FilesAdded + library.FilesReplaced, "file(s) followed");
        Add(library.FilesRenamed, "file(s) renamed");
        Add(library.FilesRemoved, "file(s) gone");
        Add(report.FilesMissing, "file(s) not found on disk");
        Add(report.HistoryAdded, "history line(s)");
        Add(report.Skipped, "series skipped (mapping)");
        Add(first.Failed + report.Failed, "series failed");
        Add(shown, "pick(s) shown to Sonarr");
        if (everySeries)
        {
            parts.Add($"episodes of {library.Total} series read");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Sonarr's grabbed lines since the checkpoint, one per episode, gathered per grab (same series and download id, or
    /// same release and date) into one <see cref="SonarrGrab"/> with every episode it covers. A grab whose lines were read
    /// by an earlier pass is not read again.
    /// </summary>
    private async Task<int> RecordGrabsAsync(FollowedSonarr follow, SonarrConnection connection, List<SonarrHistoryItem> history, Dictionary<int, SonarrSeries> series, CancellationToken ct)
    {
        var grabbed = history.Where(h => SonarrImportService.HistoryType(h.EventType) == HistoryEventType.Grabbed && h.SeriesId > 0).ToList();
        if (grabbed.Count == 0)
        {
            return 0;
        }

        var ids = grabbed.Select(h => h.Id).ToList();
        var known = (await db.SonarrGrabEpisodes.AsNoTracking().Where(e => e.Grab.FollowedSonarrId == follow.Id && ids.Contains(e.SonarrHistoryId)).Select(e => e.SonarrHistoryId).ToListAsync(ct))
            .Concat(await db.SonarrGrabs.AsNoTracking().Where(g => g.FollowedSonarrId == follow.Id && ids.Contains(g.SonarrHistoryId)).Select(g => g.SonarrHistoryId).ToListAsync(ct))
            .ToHashSet();
        var episodeCache = new Dictionary<int, Dictionary<int, SonarrEpisode>>();
        var added = 0;
        foreach (var lines in grabbed.GroupBy(GrabKey).Select(g => g.OrderBy(l => l.Id).ToList()))
        {
            if (lines.Any(l => known.Contains(l.Id)))
            {
                continue;
            }

            var record = lines[0];
            if (!series.TryGetValue(record.SeriesId, out var show))
            {
                show = await sonarr.SeriesAsync(connection, record.SeriesId, ct);
                if (show is null)
                {
                    continue;
                }

                series[show.Id] = show;
            }

            if (show.TvdbId <= 0)
            {
                continue;
            }

            var raw = new Dictionary<string, string?>(record.Data ?? [], StringComparer.OrdinalIgnoreCase);
            string? Raw(string key) => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            var title = record.SourceTitle ?? Raw("guid") ?? $"Sonarr history {record.Id}";
            var grabbedAt = Utc(record.Date);
            var publishedAt = DateTime.TryParse(Raw("publishedDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var published) ? published : (DateTime?)null;
            var infoHash = InfoHashes.Normalize(Raw("torrentInfoHash")) ?? InfoHashes.Normalize(record.DownloadId);
            var release = new ReleaseInfo
            {
                Guid = Raw("guid") ?? title,
                Title = title,
                IndexerName = Raw("indexer") ?? string.Empty,
                Size = long.TryParse(Raw("size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0,
                PublishDate = publishedAt ?? grabbedAt,
                InfoHash = infoHash,
                Flags = RadarrFollower.Flags(Raw("indexerFlags")),
                TvdbId = int.TryParse(Raw("tvdbId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var releaseTvdb) && releaseTvdb > 0 ? releaseTvdb : null,
            };

            // The episodes the grab covers, in Sonarr's (TVDB) numbering.
            var episodes = new List<SonarrGrabEpisode>();
            foreach (var line in lines.DistinctBy(l => l.EpisodeId))
            {
                var episode = line.Episode;
                if (episode is null)
                {
                    if (!episodeCache.TryGetValue(show.Id, out var all))
                    {
                        all = (await sonarr.EpisodesAsync(connection, show.Id, ct)).ToDictionary(e => e.Id);
                        episodeCache[show.Id] = all;
                    }

                    episode = all.GetValueOrDefault(line.EpisodeId);
                }

                if (episode is not null)
                {
                    episodes.Add(new SonarrGrabEpisode
                    {
                        SonarrHistoryId = line.Id,
                        SonarrEpisodeId = line.EpisodeId,
                        SeasonNumber = episode.SeasonNumber,
                        EpisodeNumber = episode.EpisodeNumber,
                        AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
                    });
                }
            }

            var releaseType = Raw("releaseType");
            var quality = SonarrQualities.ToBigarr(record.Quality?.Quality);
            db.SonarrGrabs.Add(new SonarrGrab
            {
                FollowedSonarrId = follow.Id,
                SonarrHistoryId = record.Id,
                SonarrSeriesId = record.SeriesId,
                TvdbId = show.TvdbId,
                SeriesTitle = Truncate(show.Title, 512)!,
                VersionName = follow.VersionName,
                GrabbedAt = grabbedAt,
                ReleaseTitle = Truncate(title, 1024)!,
                ReleaseGuid = Truncate(Raw("guid"), 1024),
                InfoHash = infoHash,
                DownloadId = Truncate(record.DownloadId, 128),
                IndexerName = Truncate(Raw("indexer"), 128),
                Size = release.Size,
                PublishedAt = publishedAt,
                ReleaseSource = Truncate(Raw("releaseSource"), 32),
                ReleaseType = Truncate(releaseType, 32),
                DownloadClient = Truncate(Raw("downloadClientName") ?? Raw("downloadClient"), 128),
                Quality = quality?.Name is { } name && name != "Unknown" ? name : null,
                QualityId = quality?.Id,
                RevisionVersion = record.Quality?.Revision?.Version,
                ReleaseGroup = Truncate(Raw("releaseGroup"), 255),
                Languages = (record.Languages ?? []).Select(l => l.Name).Where(n => !string.IsNullOrWhiteSpace(n) && n != "Unknown").Cast<string>().ToList(),
                CustomFormats = (record.CustomFormats ?? []).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
                // The score Sonarr gave when it grabbed; the resource's own field is recomputed with today's profile.
                CustomFormatScore = int.TryParse(Raw("customFormatScore"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : record.CustomFormatScore,
                FullSeason = string.Equals(releaseType, "SeasonPack", StringComparison.OrdinalIgnoreCase) || (releaseType is null && SeriesReleaseJudge.TryParse(title)?.FullSeason == true),
                BigarrVerdict = await judge.JudgeAsync(show.TvdbId, follow.VersionName, release, grabbedAt, ct),
                Episodes = episodes,
            });
            added++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>The lines of one grab: same series and download id, or, without a download id, same release title and time.</summary>
    private static string GrabKey(SonarrHistoryItem line) =>
        string.IsNullOrWhiteSpace(line.DownloadId)
            ? $"{line.SeriesId}|{line.SourceTitle}|{Utc(line.Date):O}"
            : $"{line.SeriesId}|{line.DownloadId}";

    /// <summary>An import, a failure or an ignore carrying the download id of a grab still in flight closes it.</summary>
    private async Task<int> ResolveAsync(FollowedSonarr follow, List<SonarrHistoryItem> history, CancellationToken ct)
    {
        var resolved = 0;
        foreach (var record in history.Where(h => !string.IsNullOrWhiteSpace(h.DownloadId)).DistinctBy(h => (h.DownloadId, h.EventType)))
        {
            RadarrGrabState? state = string.Equals(record.EventType, "downloadIgnored", StringComparison.OrdinalIgnoreCase)
                ? RadarrGrabState.Ignored
                : SonarrImportService.HistoryType(record.EventType) switch
                {
                    HistoryEventType.Imported => RadarrGrabState.Imported,
                    HistoryEventType.DownloadFailed => RadarrGrabState.Failed,
                    _ => null,
                };
            if (state is not { } next)
            {
                continue;
            }

            var at = Utc(record.Date);
            resolved += await db.SonarrGrabs
                .Where(g => g.FollowedSonarrId == follow.Id && g.DownloadId == record.DownloadId && g.State == RadarrGrabState.Grabbed && g.GrabbedAt <= at)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.State, next).SetProperty(g => g.ResolvedAt, at), ct);
        }

        return resolved;
    }

    /// <summary>Grabs of series that were not in Bigarr yet point at their series, version and episodes once the follow brought them in.</summary>
    private async Task LinkAsync(FollowedSonarr follow, CancellationToken ct)
    {
        var unlinked = await db.SonarrGrabs.Include(g => g.Episodes)
            .Where(g => g.FollowedSonarrId == follow.Id && (g.VersionId == null || g.Episodes.Any(e => e.EpisodeId == null)))
            .ToListAsync(ct);
        if (unlinked.Count == 0)
        {
            return;
        }

        var tvdbIds = unlinked.Select(g => g.TvdbId).Distinct().ToList();
        var versions = (await db.SeriesVersions.AsNoTracking()
                .Where(v => v.Name == follow.VersionName && v.Series.TvdbId != null && tvdbIds.Contains(v.Series.TvdbId.Value))
                .Select(v => new { v.Id, v.SeriesId, TvdbId = v.Series.TvdbId!.Value })
                .ToListAsync(ct))
            .GroupBy(v => v.TvdbId)
            .ToDictionary(g => g.Key, g => g.First());
        var seriesIds = versions.Values.Select(v => v.SeriesId).ToList();
        var episodes = (await db.Episodes.AsNoTracking()
                .Where(e => seriesIds.Contains(e.SeriesId))
                .Select(e => new { e.Id, e.SeriesId, e.SeasonNumber, e.EpisodeNumber })
                .ToListAsync(ct))
            .GroupBy(e => (e.SeriesId, e.SeasonNumber, e.EpisodeNumber))
            .ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var grab in unlinked)
        {
            if (!versions.TryGetValue(grab.TvdbId, out var version))
            {
                continue;
            }

            grab.VersionId = version.Id;
            grab.SeriesId = version.SeriesId;
            foreach (var episode in grab.Episodes.Where(e => e.EpisodeId is null))
            {
                episode.EpisodeId = episodes.TryGetValue((version.SeriesId, episode.SeasonNumber, episode.EpisodeNumber), out var id) ? id : null;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Bigarr's newest series picks for this Sonarr's version, shown to its <c>/parse</c>: which series and episodes it maps
    /// the title to, the quality, its custom formats and score, and whether those episodes are monitored and have a file
    /// in Sonarr. A bounded number per pass, newest first.
    /// </summary>
    private async Task<int> ShowPicksAsync(FollowedSonarr follow, SonarrConnection connection, CancellationToken ct)
    {
        var picks = await db.SeriesShadowPicks
            .Where(p => p.VersionName == follow.VersionName && p.SonarrViewAt == null)
            .OrderByDescending(p => p.PickedAt)
            .Take(PicksShownPerPass)
            .ToListAsync(ct);
        if (picks.Count == 0)
        {
            return 0;
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var pick in picks)
        {
            try
            {
                var parse = await sonarr.ParseAsync(connection, pick.ReleaseTitle, ct);
                var episodes = parse.Episodes ?? [];
                var quality = SonarrQualities.ToBigarr(parse.ParsedEpisodeInfo?.Quality?.Quality)?.Name;
                pick.SonarrView = new SonarrView(
                    parse.Series is { TvdbId: > 0 } mapped ? mapped.TvdbId : null,
                    episodes.Count == 0 ? null : EpisodesLabel(episodes.Select(e => (e.SeasonNumber, e.EpisodeNumber))),
                    quality is null or "Unknown" ? null : quality,
                    (parse.CustomFormats ?? []).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
                    parse.CustomFormatScore,
                    episodes.Count == 0 ? null : episodes.All(e => e.Monitored),
                    episodes.Count == 0 ? null : episodes.Count(e => e.HasFile));
            }
            catch (SonarrUnavailableException ex)
            {
                pick.SonarrView = new SonarrView(null, null, null, [], 0, null, null, ex.Message);
            }

            pick.SonarrViewAt = now;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return picks.Count;
    }

    /// <summary><c>S01E05</c>, or a range with its count, the way Bigarr labels the episodes of a release.</summary>
    public static string EpisodesLabel(IEnumerable<(int Season, int Episode)> episodes) =>
        SeriesReleaseJudge.Label(episodes.Distinct().OrderBy(e => e.Season).ThenBy(e => e.Episode)
            .Select(e => new Episode { Title = string.Empty, SeasonNumber = e.Season, EpisodeNumber = e.Episode })
            .ToList());

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>The follow as a scheduled task: every followed Sonarr, one after the other, every <see cref="SonarrFollower.Interval"/>.</summary>
public sealed class SonarrFollowJob(SonarrFollower follower) : IScheduledJob
{
    public const string JobName = "sonarrFollow";

    public string Name => JobName;

    public string DisplayName => "Follow Sonarr";

    public TimeSpan Interval => SonarrFollower.Interval;

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(120);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var passes = await follower.RunAllAsync(ct);
        if (passes.Count == 0)
        {
            return "No Sonarr followed";
        }

        var failed = passes.Where(p => !p.Success).ToList();
        if (failed.Count == passes.Count)
        {
            // Every Sonarr failed: the task fails too, so System → Tasks shows it.
            throw new InvalidOperationException(string.Join(" · ", failed.Select(p => $"{p.Name}: {p.Summary}")));
        }

        return string.Join(" · ", passes.Select(p => $"{p.Name}: {(p.Success ? p.Summary : "failed, " + p.Summary)}"));
    }
}
