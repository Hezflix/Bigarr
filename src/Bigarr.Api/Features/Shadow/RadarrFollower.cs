using System.Globalization;
using Bigarr.Api.Features.Migration;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Shadow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Features.Shadow;

/// <summary>The outcome of one pass over one followed Radarr.</summary>
public sealed record RadarrFollowPass(int FollowedRadarrId, string Name, bool Success, string Summary, int Grabs, int Resolved, int PicksShown, int SearchedOnAdd = 0);

/// <summary>
/// One pass over a followed Radarr (shadow mode), reading only, in this order so that Bigarr judges what Radarr did
/// against the library as it was before Radarr's download:
/// <list type="number">
/// <item>the Bigarr profiles its films map to take its profiles' qualities, cutoff and scores;</item>
/// <item>films Radarr has and Bigarr does not come in, without their file;</item>
/// <item>Radarr's grabs since the checkpoint (read from a few minutes before it, lines already read are recognised)
/// become <see cref="RadarrGrab"/> rows judged by Bigarr's engine on the spot; imports and failures close them;</item>
/// <item>a film added to Radarr since the follow started gets Bigarr's own search on add, recorded as picks;</item>
/// <item>the rest of the library follows Radarr's through the import's rules (<see cref="RadarrImportService.FollowAsync"/>):
/// files, settings, history;</item>
/// <item>Bigarr's latest picks are shown to Radarr's <c>/parse</c>.</item>
/// </list>
/// The checkpoint only moves when the whole pass went through, so a Radarr that was down is caught up from where it
/// stopped. Passes run on the schedule, on demand, and when Radarr's webhook calls (<see cref="RadarrFollowWakeups"/>).
/// </summary>
public sealed class RadarrFollower(
    BigarrDbContext db,
    RadarrClient radarr,
    RadarrImportService imports,
    RadarrFollowMemory memory,
    ShadowJudge judge,
    AutomaticSearchService search,
    IOptions<ShadowOptions> options,
    TimeProvider time,
    ILogger<RadarrFollower> logger)
{
    /// <summary>Each pass reads from this long before the checkpoint: a line written late, with an earlier date, is not missed.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

    /// <summary>Radarr's own RSS default: a pass every quarter of an hour, and within seconds when its webhook calls.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often a pass reads Radarr's whole film list (27 MB on a library of five thousand films, 6 MB gzipped): what is
    /// not in its history (a film added or deleted, a monitored flag, a profile, a folder) is caught within this time, or at
    /// once when Radarr's webhook says so. Between two, a pass reads only the films its history names.
    /// </summary>
    public static readonly TimeSpan FullListInterval = TimeSpan.FromHours(1);

    /// <summary>History naming more films than this (a big import, a bulk rename) is cheaper as one list than as one request per film.</summary>
    public const int FilmsFetchedOneByOneMax = 50;

    /// <summary>A Radarr list importing hundreds of films at once must not turn into hundreds of searches on the trackers.</summary>
    public const int SearchesOnAddPerPass = 25;

    /// <summary>Bigarr's newest picks shown to Radarr's <c>/parse</c> per pass.</summary>
    public const int PicksShownPerPass = 50;

    /// <summary>A manual pass and the scheduled one never run at the same time.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<List<RadarrFollowPass>> RunAllAsync(CancellationToken ct)
    {
        var ids = await db.FollowedRadarrs.AsNoTracking().Where(f => f.Enabled).OrderBy(f => f.Id).Select(f => f.Id).ToListAsync(ct);
        var passes = new List<RadarrFollowPass>();
        foreach (var id in ids)
        {
            passes.Add(await SyncAsync(id, ct));
        }

        return passes;
    }

    public async Task<RadarrFollowPass> SyncAsync(int id, CancellationToken ct, bool fullList = false)
    {
        await Gate.WaitAsync(ct);
        try
        {
            db.ChangeTracker.Clear();
            var follow = await db.FollowedRadarrs.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
                ?? throw new InvalidOperationException($"Followed Radarr {id} does not exist.");
            try
            {
                return await SyncCoreAsync(follow, fullList, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                // A pass that failed may have missed a film: the next one reads the whole list.
                memory.Forget(id);
                var now = time.GetUtcNow().UtcDateTime;
                var message = ex is RadarrUnavailableException ? ex.Message : $"The pass failed: {ex.Message}";
                if (ex is not RadarrUnavailableException)
                {
                    logger.LogError(ex, "Following {Name} failed", follow.Name);
                }
                else
                {
                    logger.LogWarning("Following {Name}: {Message}", follow.Name, ex.Message);
                }

                await db.FollowedRadarrs.Where(f => f.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.LastError, Truncate(message, 2048))
                    .SetProperty(f => f.LastErrorAt, now), ct);
                return new RadarrFollowPass(id, follow.Name, false, message, 0, 0, 0);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<RadarrFollowPass> SyncCoreAsync(FollowedRadarr follow, bool fullList, CancellationToken ct)
    {
        var connection = new RadarrConnection(follow.Url, follow.ApiKey);
        var history = await radarr.HistorySinceAsync(connection, follow.HistoryCheckpoint - Overlap, ct);
        var started = time.GetUtcNow().UtcDateTime;
        var remembered = memory.Get(follow.Id);
        var historyFilms = history.Select(h => h.MovieId).Distinct().ToList();
        var wholeList = fullList || remembered is null || started - remembered.ListReadAt >= FullListInterval || historyFilms.Count > FilmsFetchedOneByOneMax;
        var movies = wholeList ? await radarr.MoviesAsync(connection, ct) : await FilmsAsync(connection, historyFilms, ct);
        var filmsPerProfile = wholeList ? RadarrImportService.FilmsPerProfile(movies) : remembered!.FilmsPerProfile;
        var profiles = await radarr.ProfilesAsync(connection, ct);
        var tags = await radarr.TagsAsync(connection, ct);
        // Radarr blocklists on a failed download: the blocklist is only worth reading when one happened.
        var blocklist = history.Any(h => RadarrImportService.HistoryType(h.EventType) == HistoryEventType.DownloadFailed) ? await radarr.BlocklistAsync(connection, ct) : [];
        var byId = movies.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var data = new RadarrFollowData(profiles, tags, movies, history, blocklist);
        var request = new RadarrImportRequest(
            follow.Url,
            follow.ApiKey,
            follow.VersionName,
            follow.ProfileMap,
            follow.RootFolderMap,
            follow.ImportTags,
            ImportExclusions: false,
            ImportHistory: true,
            ImportBlocklist: true,
            ImportCustomFormats: false,
            follow.KeepMonitored,
            follow.IncludeMoviesWithoutFile);

        // 0. Radarr's profile settings, so Bigarr judges everything below the way Radarr would.
        var aligned = await imports.AlignProfilesAsync(request, data, filmsPerProfile, ct);

        // 1. New films, without their file: what Radarr grabbed for them is judged against a film that had none.
        var addedSinceFollowing = await AddedSinceFollowingAsync(follow, movies, ct);
        var added = await imports.AddNewFilmsAsync(request, data, ct);
        db.ChangeTracker.Clear();

        // 2. Radarr's grabs, judged before the follow brings in the file they led to.
        var grabs = await RecordGrabsAsync(follow, history, byId, ct);
        var resolved = await ResolveAsync(follow, history, ct);

        // 3. Bigarr's own search on add for the films Radarr was given since the follow started.
        var searched = await SearchOnAddAsync(follow, addedSinceFollowing, ct);

        // 4. Files, settings and history.
        var library = await imports.FollowAsync(request, data, ct);
        db.ChangeTracker.Clear();

        await LinkAsync(follow, ct);
        var shown = await ShowPicksAsync(follow, connection, movies, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var checkpoint = history.Count > 0 ? new[] { follow.HistoryCheckpoint, history.Max(h => Utc(h.Date)) }.Max() : follow.HistoryCheckpoint;
        var summary = Summary(grabs, resolved, added, library, searched, shown, aligned.Count);
        memory.Remember(follow.Id, wholeList ? new RadarrFollowMemory.Entry(started, filmsPerProfile) : remembered!);
        await db.FollowedRadarrs.Where(f => f.Id == follow.Id).ExecuteUpdateAsync(s => s
            .SetProperty(f => f.HistoryCheckpoint, checkpoint)
            .SetProperty(f => f.LastSyncAt, now)
            .SetProperty(f => f.LastSummary, Truncate(summary, 1024))
            .SetProperty(f => f.LastError, (string?)null)
            .SetProperty(f => f.LastErrorAt, (DateTime?)null), ct);

        logger.LogInformation("Following {Name}: {Summary}", follow.Name, summary);
        return new RadarrFollowPass(follow.Id, follow.Name, true, summary, grabs, resolved, shown, searched);
    }

    /// <summary>The films a pass between two full lists looks at: those its history names, one request each; a film Radarr no longer has is skipped.</summary>
    private async Task<List<RadarrMovie>> FilmsAsync(RadarrConnection connection, List<int> ids, CancellationToken ct)
    {
        var films = new List<RadarrMovie>();
        foreach (var id in ids)
        {
            if (await radarr.MovieAsync(connection, id, ct) is { } film)
            {
                films.Add(film);
            }
        }

        return films;
    }

    /// <summary>
    /// TMDB ids of the films Radarr was given since the follow started (its <c>added</c> date, a few minutes of margin) that
    /// Bigarr does not have yet as this version: those get Bigarr's search on add. Never a back catalogue.
    /// </summary>
    private async Task<List<int>> AddedSinceFollowingAsync(FollowedRadarr follow, List<RadarrMovie> movies, CancellationToken ct)
    {
        var since = follow.HistoryCheckpoint - Overlap;
        var recent = movies.Where(m => m.TmdbId > 0 && m.Monitored && m.Added is { } added && Utc(added) >= since).Select(m => m.TmdbId).Distinct().ToList();
        if (recent.Count == 0)
        {
            return [];
        }

        var known = await db.MovieVersions.AsNoTracking().Where(v => v.Name == follow.VersionName && recent.Contains(v.Movie.TmdbId)).Select(v => v.Movie.TmdbId).ToListAsync(ct);
        return recent.Except(known).ToList();
    }

    /// <summary>
    /// The counterpart of the search Radarr runs when Seerr or a user adds a film: Bigarr's automatic search (the rules of a
    /// search on add), in shadow mode only, so the outcome is a pick and nothing is sent. Bounded per pass.
    /// </summary>
    private async Task<int> SearchOnAddAsync(FollowedRadarr follow, List<int> tmdbIds, CancellationToken ct)
    {
        if (tmdbIds.Count == 0 || !options.Value.Enabled)
        {
            return 0;
        }

        var versionIds = await db.MovieVersions.AsNoTracking()
            .Where(v => v.Name == follow.VersionName && v.Monitored && tmdbIds.Contains(v.Movie.TmdbId))
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(ct);
        if (versionIds.Count > SearchesOnAddPerPass)
        {
            logger.LogWarning("Following {Name}: {Count} films added to Radarr, Bigarr searches the first {Bound}", follow.Name, versionIds.Count, SearchesOnAddPerPass);
        }

        var results = await search.SearchAsync(versionIds.Take(SearchesOnAddPerPass).ToList(), SearchTrigger.Added, ct);
        db.ChangeTracker.Clear();
        return results.Count;
    }

    private static string Summary(int grabs, int resolved, RadarrImportProgress added, RadarrImportProgress library, int searched, int shown, int profilesAligned)
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

        Add(profilesAligned, "profile(s) aligned with Radarr");
        Add(resolved, "grab(s) imported or failed");
        Add(first.MoviesAdded + report.MoviesAdded, "film(s) added");
        Add(first.VersionsAdded + report.VersionsAdded, "version(s) added");
        Add(searched, "film(s) searched on add");
        Add(report.VersionsUpdated, "version(s) updated");
        Add(report.FilesAdded + library.FilesReplaced, "file(s) followed");
        Add(library.FilesRenamed, "file(s) renamed");
        Add(library.FilesRemoved, "file(s) gone");
        Add(report.FilesMissing, "file(s) not found on disk");
        Add(report.HistoryAdded, "history line(s)");
        Add(report.Skipped, "film(s) skipped (mapping)");
        Add(first.Failed + report.Failed, "film(s) failed");
        Add(shown, "pick(s) shown to Radarr");
        return string.Join(", ", parts);
    }

    private async Task<int> RecordGrabsAsync(FollowedRadarr follow, List<RadarrHistoryItem> history, Dictionary<int, RadarrMovie> movies, CancellationToken ct)
    {
        var grabbed = history.Where(h => RadarrImportService.HistoryType(h.EventType) == HistoryEventType.Grabbed).ToList();
        if (grabbed.Count == 0)
        {
            return 0;
        }

        var ids = grabbed.Select(h => h.Id).ToList();
        var known = (await db.RadarrGrabs.AsNoTracking().Where(g => g.FollowedRadarrId == follow.Id && ids.Contains(g.RadarrHistoryId)).Select(g => g.RadarrHistoryId).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var record in grabbed.Where(r => !known.Contains(r.Id)))
        {
            if (!movies.TryGetValue(record.MovieId, out var movie) || movie.TmdbId <= 0)
            {
                continue;
            }

            var data = new Dictionary<string, string?>(record.Data ?? [], StringComparer.OrdinalIgnoreCase);
            string? Raw(string key) => data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            var title = record.SourceTitle ?? Raw("guid") ?? $"Radarr history {record.Id}";
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
                Flags = Flags(Raw("indexerFlags")),
            };

            var quality = record.Quality?.Quality;
            db.RadarrGrabs.Add(new RadarrGrab
            {
                FollowedRadarrId = follow.Id,
                RadarrHistoryId = record.Id,
                RadarrMovieId = record.MovieId,
                TmdbId = movie.TmdbId,
                MovieTitle = Truncate(movie.Title, 512)!,
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
                DownloadClient = Truncate(Raw("downloadClientName") ?? Raw("downloadClient"), 128),
                Quality = quality?.Name is { } name && name != "Unknown" ? name : null,
                QualityId = quality?.Id,
                RevisionVersion = record.Quality?.Revision?.Version,
                ReleaseGroup = Truncate(Raw("releaseGroup"), 255),
                Languages = (record.Languages ?? []).Select(l => l.Name).Where(n => !string.IsNullOrWhiteSpace(n) && n != "Unknown").Cast<string>().ToList(),
                CustomFormats = (record.CustomFormats ?? []).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
                // The score Radarr gave when it grabbed; the resource's own field is recomputed with today's profile.
                CustomFormatScore = int.TryParse(Raw("customFormatScore"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : record.CustomFormatScore,
                BigarrVerdict = await judge.JudgeAsync(movie.TmdbId, follow.VersionName, release, grabbedAt, ct),
            });
            added++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return added;
    }

    /// <summary>An import, a failure or an ignore carrying the download id of a grab still in flight closes it.</summary>
    private async Task<int> ResolveAsync(FollowedRadarr follow, List<RadarrHistoryItem> history, CancellationToken ct)
    {
        var resolved = 0;
        foreach (var record in history.Where(h => !string.IsNullOrWhiteSpace(h.DownloadId)))
        {
            RadarrGrabState? state = string.Equals(record.EventType, "downloadIgnored", StringComparison.OrdinalIgnoreCase)
                ? RadarrGrabState.Ignored
                : RadarrImportService.HistoryType(record.EventType) switch
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
            resolved += await db.RadarrGrabs
                .Where(g => g.FollowedRadarrId == follow.Id && g.DownloadId == record.DownloadId && g.State == RadarrGrabState.Grabbed && g.GrabbedAt <= at)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.State, next).SetProperty(g => g.ResolvedAt, at), ct);
        }

        return resolved;
    }

    /// <summary>Grabs of films that were not in Bigarr yet point at their film and version once the follow brought them in.</summary>
    private async Task LinkAsync(FollowedRadarr follow, CancellationToken ct)
    {
        var unlinked = await db.RadarrGrabs.Where(g => g.FollowedRadarrId == follow.Id && g.VersionId == null).ToListAsync(ct);
        if (unlinked.Count == 0)
        {
            return;
        }

        var tmdbIds = unlinked.Select(g => g.TmdbId).Distinct().ToList();
        var versions = (await db.MovieVersions.AsNoTracking()
                .Where(v => v.Name == follow.VersionName && tmdbIds.Contains(v.Movie.TmdbId))
                .Select(v => new { v.Id, v.MovieId, v.Movie.TmdbId })
                .ToListAsync(ct))
            .GroupBy(v => v.TmdbId)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var grab in unlinked)
        {
            if (versions.TryGetValue(grab.TmdbId, out var version))
            {
                grab.VersionId = version.Id;
                grab.MovieId = version.MovieId;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Bigarr's newest picks for this Radarr's version, shown to its <c>/parse</c>: which film it maps the title to, the
    /// quality, its custom formats and score, and the state of the film in Radarr (monitored, current file). A bounded
    /// number per pass, newest first.
    /// </summary>
    private async Task<int> ShowPicksAsync(FollowedRadarr follow, RadarrConnection connection, List<RadarrMovie> movies, CancellationToken ct)
    {
        var picks = await db.ShadowPicks
            .Where(p => p.VersionName == follow.VersionName && p.RadarrViewAt == null)
            .OrderByDescending(p => p.PickedAt)
            .Take(PicksShownPerPass)
            .ToListAsync(ct);
        if (picks.Count == 0)
        {
            return 0;
        }

        var byTmdb = movies.Where(m => m.TmdbId > 0).GroupBy(m => m.TmdbId).ToDictionary(g => g.Key, g => g.First());
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var pick in picks)
        {
            var film = byTmdb.GetValueOrDefault(pick.TmdbId);
            var file = film is { HasFile: true } ? film.MovieFile : null;
            try
            {
                var parse = await radarr.ParseAsync(connection, pick.ReleaseTitle, ct);
                if (film is null && parse.Movie is { TmdbId: > 0 } found && found.TmdbId == pick.TmdbId)
                {
                    // Not among the films this pass read: ask Radarr for that one.
                    film = await radarr.MovieAsync(connection, found.Id, ct) ?? found;
                    file = film.HasFile ? film.MovieFile : null;
                }

                var quality = parse.ParsedMovieInfo?.Quality?.Quality?.Name;
                pick.RadarrView = new RadarrView(
                    parse.Movie is { TmdbId: > 0 } mapped ? mapped.TmdbId : null,
                    quality is null or "Unknown" ? null : quality,
                    (parse.CustomFormats ?? []).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
                    parse.CustomFormatScore,
                    film?.Monitored,
                    file?.Quality?.Quality?.Name,
                    file?.CustomFormatScore);
            }
            catch (RadarrUnavailableException ex)
            {
                pick.RadarrView = new RadarrView(null, null, [], 0, film?.Monitored, file?.Quality?.Quality?.Name, file?.CustomFormatScore, ex.Message);
            }

            pick.RadarrViewAt = now;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return picks.Count;
    }

    /// <summary>Radarr writes its flags as enum names (<c>G_Freeleech, G_Internal</c>); Bigarr's carry the same values without the prefix.</summary>
    public static IndexerFlags Flags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return IndexerFlags.None;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
        {
            return (IndexerFlags)bits & Known;
        }

        var flags = IndexerFlags.None;
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = token.StartsWith("G_", StringComparison.Ordinal) ? token[2..] : token;
            if (Enum.TryParse<IndexerFlags>(name, ignoreCase: false, out var flag) && Enum.IsDefined(flag))
            {
                flags |= flag;
            }
        }

        return flags;
    }

    private static readonly IndexerFlags Known = Enum.GetValues<IndexerFlags>().Aggregate(IndexerFlags.None, (all, flag) => all | flag);

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>The follow as a scheduled task: every followed Radarr, one after the other, every <see cref="RadarrFollower.Interval"/>.</summary>
public sealed class RadarrFollowJob(RadarrFollower follower) : IScheduledJob
{
    public const string JobName = "radarrFollow";

    public string Name => JobName;

    public string DisplayName => "Follow Radarr";

    public TimeSpan Interval => RadarrFollower.Interval;

    public bool Enabled => true;

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(90);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var passes = await follower.RunAllAsync(ct);
        if (passes.Count == 0)
        {
            return "No Radarr followed";
        }

        var failed = passes.Where(p => !p.Success).ToList();
        if (failed.Count == passes.Count)
        {
            // Every Radarr failed: the task fails too, so System → Tasks shows it.
            throw new InvalidOperationException(string.Join(" · ", failed.Select(p => $"{p.Name}: {p.Summary}")));
        }

        return string.Join(" · ", passes.Select(p => $"{p.Name}: {(p.Success ? p.Summary : "failed, " + p.Summary)}"));
    }
}
