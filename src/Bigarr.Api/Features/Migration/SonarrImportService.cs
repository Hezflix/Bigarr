using System.Globalization;
using Bigarr.Api.Features.Tags;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.RadarrImport;
using Bigarr.Api.Infrastructure.SonarrImport;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Common;
using Bigarr.Domain.History;
using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Quality;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tags;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using TvSeries = Bigarr.Domain.Tv.Series;

namespace Bigarr.Api.Features.Migration;

/// <summary>What one pass of the follow reads from a Sonarr, handed to the import's per-series rules.</summary>
public sealed record SonarrFollowData(
    List<RadarrProfile> Profiles,
    List<RadarrTag> Tags,
    List<SonarrSeries> Series,
    List<SonarrHistoryItem> History,
    List<SonarrBlocklistItem> Blocklist);

/// <summary>
/// Moves a Sonarr library into Bigarr (S4), the series twin of <see cref="RadarrImportService"/>. Every Sonarr series becomes
/// one version of the show, named after the Sonarr it comes from (<c>1080p</c>, <c>4k</c>), with its monitoring (series,
/// seasons, episodes), profile, tags, season folder setting and episode files; a second Sonarr imported with another version
/// name lands as a second version of the same shows. The show is matched on its TVDB id; its metadata, seasons and
/// episodes come from Sonarr's own payload (TVDB numbering, like Bigarr's), so the import needs no TVDB round trip, and the
/// show is flagged for a refresh. Episode files are recorded with their quality, languages, release group and scene name,
/// one record per file however many episodes it holds. Sonarr's history and blocklist of each episode are copied onto the
/// episode's target. Database only: nothing on disk is touched. In shadow mode the same per-series rules follow a Sonarr
/// afterwards (<see cref="FollowAsync"/>), files included.
/// </summary>
public sealed class SonarrImportService(BigarrDbContext db, SonarrClient sonarr, TimeProvider time, ILogger<SonarrImportService> logger)
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Sonarr's id of a copied history line, so a line is never copied twice.</summary>
    public const string SonarrHistoryIdKey = "sonarrHistoryId";

    /// <summary>Per series, at most this many "file not found" lines are reported one by one.</summary>
    private const int MissingFilesReported = 3;

    public async Task<Result<SonarrImportPreviewResponse>> PreviewAsync(SonarrImportPreviewRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Connection(request.Url, request.ApiKey) is not { } connection)
        {
            return Failure.Invalid("Sonarr URL and API key are required");
        }

        Snapshot snapshot;
        int historyCount;
        int blocklistCount;
        List<SonarrLanguageProfile> languageProfiles;
        try
        {
            snapshot = await LoadAsync(connection, withHistory: false, withBlocklist: false, ct);
            historyCount = await sonarr.HistoryCountAsync(connection, ct);
            blocklistCount = await sonarr.BlocklistCountAsync(connection, ct);
            languageProfiles = await sonarr.LanguageProfilesAsync(connection, ct);
        }
        catch (SonarrUnavailableException ex)
        {
            return new Failure(StatusCodes.Status502BadGateway, "Sonarr did not answer", ex.Message);
        }

        var profiles = await db.QualityProfiles.AsNoTracking().Where(p => p.Kind == ProfileKind.Series || p.Kind == ProfileKind.Both).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        var roots = await db.RootFolders.AsNoTracking().ToListAsync(ct);
        var known = (await db.Series.AsNoTracking().Where(s => s.TvdbId != null).Select(s => s.TvdbId!.Value).ToListAsync(ct)).ToHashSet();

        var byProfile = snapshot.Series.GroupBy(s => s.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());
        var profilePreviews = snapshot.Profiles
            // A Series or Both profile of the same name, else a Series profile created from Sonarr's (0).
            .Select(p => new RadarrProfilePreview(p.Id, p.Name, byProfile.GetValueOrDefault(p.Id), profiles.FirstOrDefault(b => string.Equals(b.Name, p.Name, StringComparison.OrdinalIgnoreCase))?.Id ?? CreateProfile))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rootPaths = snapshot.RootFolders.Select(r => RadarrImportService.NormalizePath(r.Path)).Concat(snapshot.Series.Select(RootPathOf).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        var byRoot = snapshot.Series.GroupBy(s => RootPathOf(s) ?? string.Empty).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var rootPreviews = rootPaths
            .Select(path => new SonarrRootFolderPreview(
                path,
                byRoot.GetValueOrDefault(path),
                roots.FirstOrDefault(r => r.Kind == MediaKind.Series && string.Equals(RadarrImportService.NormalizePath(r.Path), path, StringComparison.Ordinal))?.Id,
                Directory.Exists(path)))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .ToList();

        var byTag = snapshot.Series.SelectMany(s => s.Tags ?? []).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var tagPreviews = snapshot.Tags.Select(t => new RadarrTagPreview(t.Id, TagService.NormalizeLabel(t.Label), byTag.GetValueOrDefault(t.Id))).OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();

        // The profile holding most series names the flavour of this Sonarr.
        var main = snapshot.Profiles.OrderByDescending(p => byProfile.GetValueOrDefault(p.Id)).FirstOrDefault();
        var suggestedName = main is not null && LooksUhd(main.Name) ? "4k" : "1080p";

        return Result<SonarrImportPreviewResponse>.Ok(new SonarrImportPreviewResponse(
            snapshot.Status.Version ?? "?",
            snapshot.Series.Count,
            snapshot.Series.Sum(s => s.Statistics?.TotalEpisodeCount ?? 0),
            snapshot.Series.Sum(s => s.Statistics?.EpisodeFileCount ?? 0),
            snapshot.Series.Count(s => s.Monitored),
            snapshot.Series.Count(s => known.Contains(s.TvdbId)),
            snapshot.Exclusions.Count,
            historyCount,
            blocklistCount,
            suggestedName,
            snapshot.Series.GroupBy(s => TypeName(SeriesTypeOf(s.SeriesType))).ToDictionary(g => g.Key, g => g.Count()),
            profilePreviews,
            rootPreviews,
            tagPreviews,
            languageProfiles.Select(l => l.Name).ToList()));
    }

    /// <summary>What must hold before an import is queued: a valid version name and mappings that point at things Bigarr has.</summary>
    /// <summary>In a profile map, the Bigarr profile id that asks the import to create a Series profile from the Sonarr one.</summary>
    public const int CreateProfile = 0;

    public async Task<Failure?> ValidateAsync(SonarrImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Connection(request.Url, request.ApiKey) is null)
        {
            return Failure.Invalid("Sonarr URL and API key are required");
        }

        if (!Slug.IsValid(request.VersionName?.Trim()))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        // 0 asks the import to create a Series profile from the Sonarr one.
        var profileIds = (request.ProfileMap ?? []).Values.Where(id => id != CreateProfile).Distinct().ToList();
        var knownProfiles = await db.QualityProfiles.Where(p => profileIds.Contains(p.Id)).Select(p => new { p.Id, p.Name, p.Kind }).ToListAsync(ct);
        if (profileIds.Except(knownProfiles.Select(p => p.Id)).Any())
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {string.Join(", ", profileIds.Except(knownProfiles.Select(p => p.Id)))}.");
        }

        if (knownProfiles.FirstOrDefault(p => !p.Kind.ForSeries()) is { } filmProfile)
        {
            return Failure.Invalid("Not a series quality profile", $"'{filmProfile.Name}' is a film profile: map Sonarr's profiles onto Series or Both profiles, or let the import create them.");
        }

        var rootIds = (request.RootFolderMap ?? []).Values.Distinct().ToList();
        var knownRoots = await db.RootFolders.Where(r => rootIds.Contains(r.Id)).Select(r => new { r.Id, r.Path, r.Kind }).ToListAsync(ct);
        if (rootIds.Except(knownRoots.Select(r => r.Id)).Any())
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {string.Join(", ", rootIds.Except(knownRoots.Select(r => r.Id)))}.");
        }

        if (knownRoots.FirstOrDefault(r => r.Kind != MediaKind.Series) is { } films)
        {
            return Failure.Invalid("Not a series root folder", $"'{films.Path}' holds films. Map Sonarr's folders onto series root folders (Settings → Library).");
        }

        return null;
    }

    public async Task ImportAsync(SonarrImportProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var request = progress.Request;
        var connection = Connection(request.Url, request.ApiKey) ?? throw new InvalidOperationException("Sonarr URL and API key are required.");
        var startedAt = time.GetUtcNow().UtcDateTime;
        var snapshot = await LoadAsync(connection, request.ImportHistory, request.ImportBlocklist, ct);

        progress.Current = "Profiles";
        request = request with { ProfileMap = await CreateProfilesAsync(request.ProfileMap ?? [], snapshot.Profiles, progress, ct) };
        if (request.ImportProfileQualities)
        {
            await ApplyProfilesAsync(request, snapshot.Profiles, SeriesPerProfile(snapshot.Series), progress, ct);
        }

        if (request.ImportQualitySizes)
        {
            progress.Current = "Quality sizes";
            await SyncSizesAsync(connection, progress, ct);
        }

        var context = await ContextAsync(request, connection, snapshot, progress, ct);

        progress.Total = snapshot.Series.Count;
        foreach (var item in snapshot.Series.OrderBy(s => s.SortTitle ?? s.Title, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            progress.Current = item.Title;
            await ImportGuardedAsync(item, context, progress, ct);
        }

        if (request.ImportExclusions)
        {
            await ImportExclusionsAsync(snapshot, progress, ct);
        }

        if (request.Follow)
        {
            await KeepFollowingAsync(request, startedAt, ct);
        }

        progress.Current = null;
    }

    private async Task ImportGuardedAsync(SonarrSeries item, ImportContext context, SonarrImportProgress progress, CancellationToken ct)
    {
        try
        {
            await ImportOneAsync(item, context, progress, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SonarrUnavailableException)
        {
            // Sonarr went away mid-run: the whole run fails (the follow keeps its checkpoint), no point in failing each series.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import of {Title} (TVDB {TvdbId}) from Sonarr failed", item.Title, item.TvdbId);
            db.ChangeTracker.Clear();
            progress.Failed();
            progress.Issue(item.Title, item.TvdbId, ex.Message, await SeriesIdAsync(item.TvdbId, ct));
        }
        finally
        {
            db.ChangeTracker.Clear();
            progress.SeriesDone();
        }
    }

    /// <summary>
    /// "Keep following this Sonarr": the import's mapping becomes a followed Sonarr whose history is read from the moment
    /// the import loaded it, so nothing between the import and the first pass is lost. A Sonarr already followed under the
    /// same URL and version keeps its checkpoint and takes the new mapping.
    /// </summary>
    private async Task KeepFollowingAsync(SonarrImportRequest request, DateTime startedAt, CancellationToken ct)
    {
        var url = request.Url.Trim();
        var versionName = request.VersionName.Trim();
        var follow = await db.FollowedSonarrs.FirstOrDefaultAsync(f => f.Url == url && f.VersionName == versionName, ct);
        if (follow is null)
        {
            var name = $"Sonarr {versionName}";
            for (var n = 2; await db.FollowedSonarrs.AnyAsync(f => f.Name == name, ct); n++)
            {
                name = $"Sonarr {versionName} {n}";
            }

            follow = new FollowedSonarr { Name = name, Url = url, ApiKey = request.ApiKey.Trim(), VersionName = versionName, HistoryCheckpoint = startedAt, CreatedAt = time.GetUtcNow().UtcDateTime };
            db.FollowedSonarrs.Add(follow);
        }

        follow.ApiKey = request.ApiKey.Trim();
        follow.ProfileMap = new Dictionary<int, int>(request.ProfileMap ?? []);
        follow.RootFolderMap = new Dictionary<string, int>(request.RootFolderMap ?? [], StringComparer.Ordinal);
        follow.ImportTags = request.ImportTags;
        follow.KeepMonitored = request.KeepMonitored;
        follow.Enabled = true;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        logger.LogInformation("Sonarr is now followed as {Name} ({Version})", follow.Name, versionName);
    }

    /// <summary>
    /// One pass of the follow (shadow mode): the series of a followed Sonarr that differ from their Bigarr version, or that
    /// have history since the last pass, go through the import's per-series rules again (Sonarr's settings win, new series,
    /// episodes and versions come in, history and blocklist are copied without duplicates), plus what a plain import leaves
    /// alone: the version's files follow Sonarr's (imported, upgraded, renamed, deleted), in the database only.
    /// <paramref name="everySeries"/> reads the episodes of every series, catching what nothing else signals (an episode's
    /// monitored flag).
    /// </summary>
    public Task<SonarrImportProgress> FollowAsync(SonarrImportRequest request, SonarrFollowData data, bool everySeries, CancellationToken ct) =>
        FollowCoreAsync(request, data, newSeriesOnly: false, everySeries, ct);

    /// <summary>
    /// The first step of a pass: the series (or versions) Sonarr has and Bigarr does not, brought in without their files
    /// nor their history. Sonarr's grabs of them are then judged, and searched for, against the show as it was before
    /// Sonarr's download; <see cref="FollowAsync"/> brings the files and the history right after.
    /// </summary>
    public Task<SonarrImportProgress> AddNewSeriesAsync(SonarrImportRequest request, SonarrFollowData data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FollowCoreAsync(request with { ImportHistory = false, ImportBlocklist = false }, data, newSeriesOnly: true, everySeries: false, ct);
    }

    private async Task<SonarrImportProgress> FollowCoreAsync(SonarrImportRequest request, SonarrFollowData data, bool newSeriesOnly, bool everySeries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(data);

        var connection = Connection(request.Url, request.ApiKey) ?? throw new InvalidOperationException("Sonarr URL and API key are required.");
        var progress = new SonarrImportProgress(request) { State = RadarrImportState.Running, StartedAt = time.GetUtcNow().UtcDateTime };
        var series = data.Series.Where(s => s.TvdbId > 0 && !string.IsNullOrWhiteSpace(s.Title)).ToList();
        var snapshot = new Snapshot(
            new RadarrSystemStatus(null, null, null), data.Profiles, [], data.Tags, series, [],
            data.History.ToLookup(h => h.SeriesId), data.Blocklist.ToLookup(b => b.SeriesId));

        var changed = await ChangedAsync(snapshot, request, newSeriesOnly, everySeries, ct);
        progress.Total = changed.Count;
        if (changed.Count == 0)
        {
            return progress;
        }

        var context = await ContextAsync(request, connection, snapshot with { Series = changed }, progress, ct) with { Follow = true, SkipFiles = newSeriesOnly };
        foreach (var item in changed)
        {
            ct.ThrowIfCancellationRequested();
            progress.Current = item.Title;
            await ImportGuardedAsync(item, context, progress, ct);
        }

        progress.Current = null;
        return progress;
    }

    /// <summary>
    /// The Sonarr series the follow has to look at: not in Bigarr yet as this version, or with a monitored flag, profile,
    /// folder, tags, season settings, file count or size that differs, or with history lines in this pass. Only the first
    /// kind when <paramref name="newSeriesOnly"/>; every series when <paramref name="everySeries"/>.
    /// </summary>
    private async Task<List<SonarrSeries>> ChangedAsync(Snapshot snapshot, SonarrImportRequest request, bool newSeriesOnly, bool everySeries, CancellationToken ct)
    {
        var versionName = request.VersionName.Trim();
        var bigarr = (await db.SeriesVersions.AsNoTracking()
                .Where(v => v.Name == versionName && v.Series.TvdbId != null)
                .Select(v => new
                {
                    TvdbId = v.Series.TvdbId!.Value,
                    v.Monitored,
                    v.QualityProfileId,
                    v.FolderName,
                    v.SeasonFolder,
                    v.MonitorNewItems,
                    Tags = v.Tags.Select(t => t.Label).ToList(),
                    Seasons = v.Seasons.Select(s => new { s.Season.SeasonNumber, s.Monitored }).ToList(),
                    Files = v.Files.Count,
                    Size = v.Files.Sum(f => (long?)f.Size) ?? 0,
                })
                .ToListAsync(ct))
            .GroupBy(v => v.TvdbId)
            .ToDictionary(g => g.Key, g => g.First());
        var tagLabels = snapshot.Tags.ToDictionary(t => t.Id, t => TagService.NormalizeLabel(t.Label));
        var profileMap = request.ProfileMap ?? [];

        var changed = new List<SonarrSeries>();
        foreach (var item in snapshot.Series)
        {
            if (!bigarr.TryGetValue(item.TvdbId, out var version))
            {
                changed.Add(item);
                continue;
            }

            if (newSeriesOnly)
            {
                continue;
            }

            if (everySeries || snapshot.History[item.Id].Any())
            {
                changed.Add(item);
                continue;
            }

            var tags = request.ImportTags
                ? (item.Tags ?? []).Select(id => tagLabels.GetValueOrDefault(id) ?? string.Empty).Where(l => l.Length is > 0 and <= TagService.MaxLabelLength).ToHashSet(StringComparer.Ordinal)
                : null;
            var seasons = version.Seasons.ToDictionary(s => s.SeasonNumber, s => s.Monitored);
            var seasonsDiffer = (item.Seasons ?? []).Any(s => seasons.GetValueOrDefault(s.SeasonNumber) != (request.KeepMonitored && s.Monitored));
            var settingsDiffer = version.Monitored != (request.KeepMonitored && item.Monitored)
                || (profileMap.TryGetValue(item.QualityProfileId, out var profileId) && profileId != version.QualityProfileId)
                || (RadarrImportService.LastSegment(item.Path) is { } folder && folder != version.FolderName)
                || version.SeasonFolder != item.SeasonFolder
                || version.MonitorNewItems != NewItems(item.MonitorNewItems)
                || (tags is not null && !tags.SetEquals(version.Tags))
                || seasonsDiffer;
            var filesDiffer = item.Statistics is { } stats && (stats.EpisodeFileCount != version.Files || (stats.SizeOnDisk > 0 && stats.SizeOnDisk != version.Size));
            if (settingsDiffer || filesDiffer)
            {
                changed.Add(item);
            }
        }

        return changed;
    }

    private async Task ImportOneAsync(SonarrSeries item, ImportContext context, SonarrImportProgress progress, CancellationToken ct)
    {
        if (!context.Profiles.TryGetValue(item.QualityProfileId, out var profileId))
        {
            var name = context.Snapshot.Profiles.FirstOrDefault(p => p.Id == item.QualityProfileId)?.Name ?? item.QualityProfileId.ToString(CultureInfo.InvariantCulture);
            await SkipAsync(progress, item, $"Quality profile '{name}' is not mapped.", ct);
            return;
        }

        var sonarrRoot = RootPathOf(item);
        if (sonarrRoot is null || !context.Roots.TryGetValue(sonarrRoot, out var root))
        {
            await SkipAsync(progress, item, $"Root folder '{sonarrRoot ?? "?"}' is not mapped.", ct);
            return;
        }

        var episodes = (await sonarr.EpisodesAsync(context.Connection, item.Id, ct)).Where(e => e.SeasonNumber >= 0 && e.EpisodeNumber >= 0).ToList();
        var files = context.SkipFiles ? [] : await sonarr.EpisodeFilesAsync(context.Connection, item.Id, ct);

        var now = time.GetUtcNow().UtcDateTime;
        var keep = context.Request.KeepMonitored;
        var monitored = keep && item.Monitored;
        var tags = context.Request.ImportTags ? await TagsForAsync(item.Tags, context, ct) : [];
        var folderName = RadarrImportService.LastSegment(item.Path) ?? FolderNameBuilder.Build(item.Title, item.Year > 0 ? item.Year : null);

        var series = await db.Series
            .Include(s => s.Seasons)
            .Include(s => s.Versions).ThenInclude(v => v.Tags)
            .Include(s => s.Versions).ThenInclude(v => v.Seasons)
            .Include(s => s.Episodes).ThenInclude(e => e.Targets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.TvdbId == item.TvdbId, ct);

        var newSeries = false;
        if (series is null)
        {
            series = NewSeries(item, root.Id, now);
            db.Series.Add(series);
            newSeries = true;
        }
        else
        {
            // The files of every version are tracked: a file that goes must unlink its episodes.
            await db.EpisodeFiles.Where(f => f.Version.SeriesId == series.Id).LoadAsync(ct);
        }

        // Seasons and episodes Sonarr knows and Bigarr does not (yet) come in from Sonarr's payload.
        var seasons = series.Seasons.ToDictionary(s => s.SeasonNumber);
        foreach (var number in (item.Seasons ?? []).Select(s => s.SeasonNumber).Concat(episodes.Select(e => e.SeasonNumber)).Where(n => n >= 0).Distinct().Order())
        {
            if (!seasons.ContainsKey(number))
            {
                var season = new Season { Series = series, SeasonNumber = number };
                series.Seasons.Add(season);
                seasons[number] = season;
                foreach (var other in series.Versions)
                {
                    other.Seasons.Add(new SeriesVersionSeason { Version = other, Season = season, Monitored = Features.Series.MonitoringRules.NewSeasonMonitored(other, number) });
                }
            }
        }

        var byTvdb = series.Episodes.Where(e => e.TvdbId is not null).GroupBy(e => e.TvdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        var byNumber = series.Episodes.GroupBy(e => (e.SeasonNumber, e.EpisodeNumber)).ToDictionary(g => g.Key, g => g.First());
        var bySonarr = new Dictionary<int, Episode>();
        var addedEpisodes = 0;
        foreach (var info in episodes)
        {
            if (!(info.TvdbId > 0 && byTvdb.TryGetValue(info.TvdbId, out var episode)) && !byNumber.TryGetValue((info.SeasonNumber, info.EpisodeNumber), out episode))
            {
                episode = NewEpisode(info, series, seasons[info.SeasonNumber]);
                series.Episodes.Add(episode);
                byNumber[(info.SeasonNumber, info.EpisodeNumber)] = episode;
                addedEpisodes++;
                foreach (var other in series.Versions)
                {
                    var seasonMonitored = other.Seasons.FirstOrDefault(s => s.Season.SeasonNumber == info.SeasonNumber)?.Monitored ?? false;
                    other.Episodes.Add(new EpisodeTarget { Version = other, Episode = episode, Monitored = info.SeasonNumber > 0 && seasonMonitored, CreatedAt = now });
                }
            }
            else
            {
                // Sonarr's numbering is the reference for an anime it already numbers; Bigarr's own metadata stays otherwise.
                episode.AbsoluteEpisodeNumber ??= info.AbsoluteEpisodeNumber;
            }

            bySonarr[info.Id] = episode;
        }

        progress.EpisodesAdded(addedEpisodes);

        // The version: Sonarr's settings win, the folder only moves when following.
        var settingsChanged = false;
        var newVersion = false;
        var version = series.Versions.FirstOrDefault(v => v.Name == context.VersionName);
        if (version is null)
        {
            version = new SeriesVersion
            {
                Series = series,
                Name = context.VersionName,
                Mode = VersionMode.Managed,
                QualityProfileId = profileId,
                RootFolderId = root.Id,
                FolderName = folderName,
                IsDefault = series.Versions.All(v => !v.IsDefault),
                Monitored = monitored,
                SeasonFolder = item.SeasonFolder,
                MonitorNewItems = NewItems(item.MonitorNewItems),
                Tags = tags,
                CreatedAt = now,
            };
            series.Versions.Add(version);
            newVersion = true;
        }
        else
        {
            settingsChanged = version.Monitored != monitored
                || version.QualityProfileId != profileId
                || version.SeasonFolder != item.SeasonFolder
                || version.MonitorNewItems != NewItems(item.MonitorNewItems)
                || (context.Request.ImportTags && !version.Tags.Select(t => t.Id).ToHashSet().SetEquals(tags.Select(t => t.Id)));
            version.Monitored = monitored;
            version.QualityProfileId = profileId;
            version.SeasonFolder = item.SeasonFolder;
            version.MonitorNewItems = NewItems(item.MonitorNewItems);
            if (context.Request.ImportTags)
            {
                version.Tags.Clear();
                foreach (var tag in tags)
                {
                    version.Tags.Add(tag);
                }
            }

            if (context.Follow && version.FolderName != folderName)
            {
                version.FolderName = folderName;
                settingsChanged = true;
            }
        }

        // Season flags and one target per episode for this version, with Sonarr's monitored flags.
        var sonarrSeasons = (item.Seasons ?? []).ToDictionary(s => s.SeasonNumber, s => keep && s.Monitored);
        foreach (var season in series.Seasons)
        {
            var flag = version.Seasons.FirstOrDefault(s => ReferenceEquals(s.Season, season) || (s.SeasonId == season.Id && season.Id > 0));
            var wanted = sonarrSeasons.TryGetValue(season.SeasonNumber, out var m) ? m : flag?.Monitored ?? false;
            if (flag is null)
            {
                version.Seasons.Add(new SeriesVersionSeason { Version = version, Season = season, Monitored = wanted });
            }
            else if (flag.Monitored != wanted)
            {
                flag.Monitored = wanted;
                settingsChanged = true;
            }
        }

        var targets = new Dictionary<Episode, EpisodeTarget>(ReferenceEqualityComparer.Instance);
        foreach (var target in version.Episodes.Concat(series.Episodes.SelectMany(e => e.Targets).Where(t => ReferenceEquals(t.Version, version) || (t.VersionId == version.Id && version.Id > 0))))
        {
            if (target.Episode is { } episode)
            {
                targets.TryAdd(episode, target);
            }
        }

        foreach (var episode in series.Episodes)
        {
            if (!targets.ContainsKey(episode))
            {
                var target = new EpisodeTarget { Version = version, Episode = episode, Monitored = false, CreatedAt = now };
                version.Episodes.Add(target);
                targets[episode] = target;
            }
        }

        foreach (var info in episodes)
        {
            var target = targets[bySonarr[info.Id]];
            var wanted = keep && info.Monitored;
            if (target.Monitored != wanted)
            {
                target.Monitored = wanted;
                settingsChanged |= !newVersion;
            }
        }

        // Before the files move: which episodes had a file, and of what quality, for the history's upgrade rule.
        var before = targets.Values.ToDictionary<EpisodeTarget, EpisodeTarget, (bool HadFile, string? Quality)>(t => t, t => (t.EpisodeFile is not null, t.EpisodeFile is { } f ? RadarrImportService.QualityName(f.QualityId) : null), ReferenceEqualityComparer.Instance);

        var notes = new List<string>();
        if (!context.SkipFiles)
        {
            SyncFiles(item, version, files, episodes, bySonarr, targets, context, progress, notes, now);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Importing a series is a deliberate act: it lifts a previous exclusion, like adding it by hand.
        await db.SeriesExclusions.Where(e => e.TvdbId == item.TvdbId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        // Ids exist now: the history lines and blocklist entries can point at the episode targets.
        var targetsBySonarr = bySonarr.ToDictionary(kv => kv.Key, kv => targets[kv.Value]);
        var episodesBySonarr = bySonarr;
        var historyAdded = context.Request.ImportHistory
            ? await AddHistoryAsync(item, series, version, targetsBySonarr, episodesBySonarr, context, context.Follow ? before : null, ct)
            : 0;
        var blocklistAdded = context.Request.ImportBlocklist ? await AddBlocklistAsync(item, targetsBySonarr, context, ct) : 0;

        if (newSeries)
        {
            progress.SeriesAdded();
            progress.VersionAdded();
        }
        else if (newVersion)
        {
            progress.VersionAdded();
        }
        else if (!context.Follow || settingsChanged)
        {
            progress.VersionUpdated();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        progress.HistoryAdded(historyAdded);
        progress.BlocklistAdded(blocklistAdded);
        foreach (var note in notes)
        {
            progress.Issue(item.Title, item.TvdbId, note, series.Id);
        }
    }

    /// <summary>
    /// Sonarr's episode files onto the version: a file Bigarr already records at the same path stays (its quality and
    /// languages follow Sonarr's), a new one is recorded when it is on disk, and the episodes Sonarr says it holds point at
    /// it (a multi-episode file holds several). When following, a Bigarr file Sonarr no longer lists goes (or is the same
    /// file renamed: same size, same episodes), and an episode Sonarr has no file for loses its own.
    /// </summary>
    private void SyncFiles(
        SonarrSeries item,
        SeriesVersion version,
        List<SonarrEpisodeFile> files,
        List<SonarrEpisode> episodes,
        Dictionary<int, Episode> bySonarr,
        Dictionary<Episode, EpisodeTarget> targets,
        ImportContext context,
        SonarrImportProgress progress,
        List<string> notes,
        DateTime now)
    {
        var existing = version.Files.ToList();
        var byPath = existing.GroupBy(f => f.RelativePath, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var episodesByFile = episodes.Where(e => e.EpisodeFileId > 0).ToLookup(e => e.EpisodeFileId);
        var kept = new HashSet<EpisodeFile>(ReferenceEqualityComparer.Instance);
        var rootPath = context.RootPaths.GetValueOrDefault(version.RootFolderId) ?? context.Roots.Values.FirstOrDefault(r => r.Id == version.RootFolderId)?.Path ?? string.Empty;
        var sonarrPaths = files.Select(f => RelativePathOf(f, item)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var missing = 0;

        foreach (var file in files)
        {
            var holders = episodesByFile[file.Id].Select(e => bySonarr.GetValueOrDefault(e.Id)).OfType<Episode>().Select(e => targets[e]).ToList();
            var relative = RelativePathOf(file, item);
            if (relative is null)
            {
                progress.FileMissing();
                notes.Add($"Sonarr lists a file without a path (S{file.SeasonNumber:00}).");
                continue;
            }

            if (!byPath.TryGetValue(relative, out var record))
            {
                var full = Path.Combine(rootPath, version.FolderName, relative);
                if (!File.Exists(full))
                {
                    progress.FileMissing();
                    if (++missing <= MissingFilesReported)
                    {
                        notes.Add($"File not found: {full}");
                    }

                    continue;
                }

                // The same file under a new name: a record of this version Sonarr no longer lists, same size, same episodes.
                var renamed = context.Follow
                    ? existing.FirstOrDefault(f => !kept.Contains(f) && !sonarrPaths.Contains(f.RelativePath) && f.Size == file.Size
                        && holders.Count > 0 && holders.All(t => ReferenceEquals(t.EpisodeFile, f)))
                    : null;
                if (renamed is not null)
                {
                    renamed.RelativePath = relative;
                    record = renamed;
                    progress.FileRenamed();
                }
                else
                {
                    record = BuildFile(file, relative, full, context, now);
                    version.Files.Add(record);
                    if (holders.Any(t => t.EpisodeFile is not null))
                    {
                        progress.FileReplaced();
                    }
                    else
                    {
                        progress.FileAdded();
                    }
                }

                byPath[relative] = record;
            }
            else
            {
                Refresh(record, file, context);
            }

            kept.Add(record);
            foreach (var target in holders)
            {
                target.EpisodeFile = record;
            }
        }

        if (missing > MissingFilesReported)
        {
            notes.Add($"{missing - MissingFilesReported} more file(s) not found on disk.");
        }

        if (!context.Follow)
        {
            return;
        }

        // Following: an episode Sonarr has no file for has none here either; a record nothing points at any more goes.
        var withFile = episodes.Where(e => e.EpisodeFileId > 0 && files.Any(f => f.Id == e.EpisodeFileId)).Select(e => bySonarr.GetValueOrDefault(e.Id)).OfType<Episode>().ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var info in episodes.Where(e => e.EpisodeFileId <= 0 || files.All(f => f.Id != e.EpisodeFileId)))
        {
            if (bySonarr.GetValueOrDefault(info.Id) is { } episode && !withFile.Contains(episode) && targets[episode].EpisodeFile is not null)
            {
                targets[episode].EpisodeFile = null;
                targets[episode].EpisodeFileId = null;
            }
        }

        foreach (var gone in existing.Where(f => !kept.Contains(f)))
        {
            foreach (var target in targets.Values.Where(t => ReferenceEquals(t.EpisodeFile, gone) || (t.EpisodeFileId == gone.Id && gone.Id > 0)))
            {
                target.EpisodeFile = null;
                target.EpisodeFileId = null;
            }

            version.Files.Remove(gone);
            db.EpisodeFiles.Remove(gone);
            progress.FileRemoved();
        }
    }

    /// <summary>Sonarr re-read the file (a new quality, a language): the record follows, the path stays.</summary>
    private static void Refresh(EpisodeFile record, SonarrEpisodeFile file, ImportContext context)
    {
        if (QualityIdOf(file, record.RelativePath, context.QualityIds) is var quality && quality != record.QualityId && file.Quality?.Quality is not null)
        {
            record.QualityId = quality;
        }

        if (file.Size > 0)
        {
            record.Size = file.Size;
        }

        record.ReleaseGroup = NullIfBlank(file.ReleaseGroup) ?? record.ReleaseGroup;
        record.OriginalReleaseTitle = NullIfBlank(file.SceneName) ?? record.OriginalReleaseTitle;
        var languages = Languages(file);
        if (languages.Count > 0)
        {
            record.Languages = languages;
        }
    }

    private static EpisodeFile BuildFile(SonarrEpisodeFile file, string relativePath, string fullPath, ImportContext context, DateTime now)
    {
        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
        }
        catch (IOException)
        {
            size = file.Size;
        }

        return new EpisodeFile
        {
            RelativePath = relativePath,
            SeasonNumber = file.SeasonNumber,
            Size = size > 0 ? size : file.Size,
            DateAdded = file.DateAdded is { } added ? Utc(added) : now,
            QualityId = QualityIdOf(file, fullPath, context.QualityIds),
            Revision = new Revision
            {
                Version = file.Quality?.Revision?.Version ?? 1,
                Real = file.Quality?.Revision?.Real ?? 0,
                IsRepack = file.Quality?.Revision?.IsRepack ?? false,
            },
            ReleaseGroup = NullIfBlank(file.ReleaseGroup),
            Languages = Languages(file),
            IndexerFlags = (IndexerFlags)file.IndexerFlags,
            OriginalReleaseTitle = NullIfBlank(file.SceneName) ?? Path.GetFileNameWithoutExtension(relativePath),
        };
    }

    private static List<string> Languages(SonarrEpisodeFile file) =>
        (file.Languages ?? (file.Language is { } one ? [one] : []))
            .Select(l => l.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n) && n != "Unknown")
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Sonarr's quality translated to Bigarr's ids; one Bigarr does not know falls back to what the file name says.</summary>
    private static int QualityIdOf(SonarrEpisodeFile file, string path, HashSet<int> knownIds)
    {
        if (SonarrQualities.ToBigarr(file.Quality?.Quality) is { Id: > 0 } quality && knownIds.Contains(quality.Id))
        {
            return quality.Id;
        }

        try
        {
            var parsed = Bigarr.Parsing.Episodes.QualityParser.ParseQuality(Path.GetFileName(path))?.Quality;
            if (parsed is not null && knownIds.Contains(parsed.Id))
            {
                return parsed.Id;
            }
        }
        catch (Exception)
        {
            // an unparsable name is not an error
        }

        return knownIds.Contains(0) ? 0 : knownIds.Min();
    }

    private static TvSeries NewSeries(SonarrSeries item, int rootFolderId, DateTime now)
    {
        var images = new List<MediaImage>();
        foreach (var image in item.Images ?? [])
        {
            var url = image.RemoteUrl ?? (image.Url is { } u && u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null);
            if (url is null)
            {
                continue;
            }

            if (string.Equals(image.CoverType, "poster", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new MediaImage(ImageType.Poster, url));
            }
            else if (string.Equals(image.CoverType, "fanart", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(new MediaImage(ImageType.Fanart, url));
            }
        }

        var title = item.Title.Trim();
        return new TvSeries
        {
            TvdbId = item.TvdbId,
            TmdbId = item.TmdbId > 0 ? item.TmdbId : null,
            ImdbId = NullIfBlank(item.ImdbId),
            Title = title,
            SortTitle = SortTitle.Build(title),
            Year = item.Year > 0 ? item.Year : null,
            Overview = NullIfBlank(item.Overview),
            Status = Status(item.Status, item.Ended),
            Network = NullIfBlank(item.Network),
            Runtime = item.Runtime > 0 ? item.Runtime : null,
            AirTime = NullIfBlank(item.AirTime),
            OriginalCountry = NullIfBlank(item.OriginalCountry)?.ToLowerInvariant(),
            OriginalLanguage = RadarrImportService.IsoCode(item.OriginalLanguage?.Name),
            SeriesType = SeriesTypeOf(item.SeriesType),
            Genres = (item.Genres ?? []).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList(),
            // Scene names standing for one season are XEM's business (refreshed with the show); the others are plain alternates.
            AlternativeTitles = (item.AlternateTitles ?? [])
                .Where(t => t.SceneSeasonNumber is null && t.SeasonNumber is null)
                .Select(t => t.Title?.Trim())
                .Where(t => !string.IsNullOrEmpty(t) && !string.Equals(t, title, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(t => new AlternativeTitle(t!, null))
                .ToList(),
            Images = images,
            FirstAired = item.FirstAired is { } first ? DateOnly.FromDateTime(first) : null,
            LastAired = item.LastAired is { } last ? DateOnly.FromDateTime(last) : null,
            DefaultRootFolderId = rootFolderId,
            AddedAt = item.Added is { } added ? Utc(added) : now,
            UseSceneNumbering = item.UseSceneNumbering,
            // Sonarr's snapshot is good enough to start; a refresh normalizes it against TVDB/TMDB and XEM.
            LastMetadataRefreshAt = null,
            SceneNumberingRefreshedAt = null,
        };
    }

    private static Episode NewEpisode(SonarrEpisode info, TvSeries series, Season season)
    {
        var airDate = DateOnly.TryParseExact(info.AirDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null;
        var scene = series.UseSceneNumbering && info.SceneSeasonNumber is not null;
        return new Episode
        {
            Series = series,
            Season = season,
            TvdbId = info.TvdbId > 0 ? info.TvdbId : null,
            SeasonNumber = info.SeasonNumber,
            EpisodeNumber = info.EpisodeNumber,
            AbsoluteEpisodeNumber = info.SeasonNumber > 0 ? info.AbsoluteEpisodeNumber : null,
            SceneSeasonNumber = scene ? info.SceneSeasonNumber : null,
            SceneEpisodeNumber = scene ? info.SceneEpisodeNumber : null,
            SceneAbsoluteEpisodeNumber = scene ? info.SceneAbsoluteEpisodeNumber : null,
            UnverifiedSceneNumbering = scene && info.UnverifiedSceneNumbering,
            AirDate = airDate,
            AirDateUtc = info.AirDateUtc is { } utc ? Utc(utc) : AirTimes.ToUtc(airDate, series.AirTime, series.OriginalCountry),
            Title = string.IsNullOrWhiteSpace(info.Title) ? "TBA" : info.Title.Trim(),
            Overview = NullIfBlank(info.Overview),
            Runtime = info.Runtime > 0 ? info.Runtime : null,
        };
    }

    /// <summary>
    /// Sonarr profiles mapped to <see cref="CreateProfile"/> get a Bigarr profile of kind Series named after them (made
    /// unique), shaped like the seeded series profile until the Sonarr profile's qualities are copied onto it. Returns the
    /// map with the new ids, which a "keep following" then keeps.
    /// </summary>
    private async Task<Dictionary<int, int>> CreateProfilesAsync(Dictionary<int, int> map, List<RadarrProfile> sonarrProfiles, SonarrImportProgress progress, CancellationToken ct)
    {
        var result = new Dictionary<int, int>(map);
        var wanted = map.Where(kv => kv.Value == CreateProfile).Select(kv => kv.Key).ToList();
        if (wanted.Count == 0)
        {
            return result;
        }

        var names = (await db.QualityProfiles.AsNoTracking().Select(p => p.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = Bigarr.Parsing.Qualities.Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ThenBy(d => d.Quality.Id).ToList();
        foreach (var sonarrId in wanted)
        {
            var source = sonarrProfiles.FirstOrDefault(p => p.Id == sonarrId);
            var baseName = string.IsNullOrWhiteSpace(source?.Name) ? $"Sonarr {sonarrId}" : source.Name.Trim();
            var name = names.Contains(baseName) ? $"{baseName} (series)" : baseName;
            for (var n = 2; names.Contains(name); n++)
            {
                name = $"{baseName} (series {n})";
            }

            var profile = Features.Quality.QualitySeeder.BuildSeriesHdProfile(definitions);
            profile.Name = name;
            profile.IsSystem = false;
            profile.Kind = ProfileKind.Series;
            db.QualityProfiles.Add(profile);
            await db.SaveChangesAsync(ct);
            names.Add(name);
            result[sonarrId] = profile.Id;
            progress.ProfileCreated();
            logger.LogInformation("Quality profile {Name} created from Sonarr's", name);
        }

        db.ChangeTracker.Clear();
        return result;
    }

    /// <summary>
    /// Sonarr's quality sizes become Bigarr's series sizes (films keep theirs), on the import and on every pass of the
    /// follow. A quality Sonarr does not list keeps its series sizes. Returns how many definitions changed.
    /// </summary>
    public async Task<int> SyncSizesAsync(SonarrConnection connection, SonarrImportProgress? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var sonarrDefinitions = await sonarr.QualityDefinitionsAsync(connection, ct);
        if (sonarrDefinitions.Count == 0)
        {
            return 0;
        }

        var definitions = await db.QualityDefinitions.ToDictionaryAsync(q => q.Id, ct);
        var changed = 0;
        foreach (var item in sonarrDefinitions)
        {
            if (SonarrQualities.ToBigarr(item.Quality) is not { } quality || !definitions.TryGetValue(quality.Id, out var definition))
            {
                continue;
            }

            if (definition.SeriesMinSize == item.MinSize && definition.SeriesPreferredSize == item.PreferredSize && definition.SeriesMaxSize == item.MaxSize)
            {
                continue;
            }

            definition.SeriesMinSize = item.MinSize;
            definition.SeriesPreferredSize = item.PreferredSize;
            definition.SeriesMaxSize = item.MaxSize;
            changed++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (changed > 0)
        {
            progress?.QualitySizesUpdated(changed);
            logger.LogInformation("{Count} series quality size(s) taken from Sonarr", changed);
        }

        return changed;
    }

    /// <summary>
    /// The Series profiles the import (and every pass of the follow) maps Sonarr's series to take the qualities, cutoff,
    /// score thresholds and format scores (formats matched by name, none created) of the Sonarr profile holding most of
    /// their series. A Both profile keeps its own settings: the Radarr follow does not write it either. Returns the names
    /// of the profiles that changed.
    /// </summary>
    public async Task<List<string>> ApplyProfilesAsync(SonarrImportRequest request, List<RadarrProfile> sonarrProfiles, Dictionary<int, int> seriesPerProfile, SonarrImportProgress? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sonarrProfiles);

        var formats = (await db.CustomFormats.ToListAsync(ct))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var qualityIds = await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct);
        var profiles = sonarrProfiles.Select(SonarrQualities.ToBigarr).ToList();
        var changed = new List<string>();
        foreach (var group in (request.ProfileMap ?? []).GroupBy(kv => kv.Value))
        {
            var sources = RadarrImportService.Sources(group.Select(kv => kv.Key), profiles, seriesPerProfile);
            if (sources.Count == 0 || await db.QualityProfiles.Include(p => p.FormatScores).FirstOrDefaultAsync(p => p.Id == group.Key, ct) is not { } profile)
            {
                continue;
            }

            if (profile.Kind != ProfileKind.Series)
            {
                // A Both profile is shared with films: neither follow rewrites it.
                progress?.Issue(profile.Name, null, $"'{profile.Name}' is a Films and Series profile: it keeps its own settings. Only Series profiles take a Sonarr profile's qualities and scores.");
                continue;
            }

            var key = RadarrImportService.ProfileKey(profile);
            if (sources.FirstOrDefault(p => p.Items is not null) is { } shaped)
            {
                if (RadarrProfileQualities.Translate(shaped, qualityIds) is { } qualities)
                {
                    RadarrProfileQualities.Apply(profile, qualities);
                    progress?.ProfileQualitiesCopied();
                }
                else
                {
                    progress?.Issue(profile.Name, null, $"The qualities of Sonarr profile '{shaped.Name}' could not be copied (nothing allowed, or a cutoff that is not allowed): '{profile.Name}' keeps its own.");
                }

                var differing = sources.Where(other => other != shaped && other.Items is not null && RadarrImportService.QualityKey(other) != RadarrImportService.QualityKey(shaped)).Select(other => $"'{other.Name}'").ToList();
                if (differing.Count > 0)
                {
                    progress?.Issue(profile.Name, null, $"Sonarr profiles '{shaped.Name}' and {string.Join(", ", differing)} all map to '{profile.Name}' with different qualities: '{shaped.Name}' holds the most series, its qualities were kept.");
                }
            }

            if (sources.FirstOrDefault(p => p.FormatItems is not null) is { } scored)
            {
                RadarrImportService.ApplyScores(profile, scored, formats);
                progress?.ProfileScored();
            }

            if (RadarrImportService.ProfileKey(profile) != key)
            {
                changed.Add(profile.Name);
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        if (changed.Count > 0)
        {
            logger.LogInformation("Profiles aligned with Sonarr: {Profiles}", string.Join(", ", changed));
        }

        return changed;
    }

    /// <summary>How many series each Sonarr profile holds, which tells the profile of most series apart when several map to one.</summary>
    public static Dictionary<int, int> SeriesPerProfile(IEnumerable<SonarrSeries> series) =>
        series.GroupBy(s => s.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());

    private async Task<int?> SeriesIdAsync(int tvdbId, CancellationToken ct) =>
        await db.Series.AsNoTracking().Where(s => s.TvdbId == tvdbId).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Sonarr's lines about one series, as journal lines of the episode targets of the version. A line already copied by
    /// an earlier run (its Sonarr id, or the same type, date and release on the same episode) is not copied twice, so the
    /// import can be re-run and the follow can read a little before its checkpoint.
    /// </summary>
    private async Task<int> AddHistoryAsync(
        SonarrSeries item,
        TvSeries series,
        SeriesVersion version,
        Dictionary<int, EpisodeTarget> targets,
        Dictionary<int, Episode> episodes,
        ImportContext context,
        Dictionary<EpisodeTarget, (bool HadFile, string? Quality)>? before,
        CancellationToken ct)
    {
        var records = context.Snapshot.History[item.Id].Where(r => targets.ContainsKey(r.EpisodeId)).ToList();
        if (records.Count == 0)
        {
            return 0;
        }

        var targetIds = targets.Values.Select(t => t.Id).ToList();
        var lines = await db.History.AsNoTracking()
            .Where(h => h.TargetId != null && targetIds.Contains(h.TargetId.Value))
            .Select(h => new { h.TargetId, h.EventType, h.CreatedAt, h.SourceTitle, h.Data })
            .ToListAsync(ct);
        var known = lines.Select(h => (h.TargetId!.Value, h.EventType, h.CreatedAt.Ticks, h.SourceTitle ?? string.Empty)).ToHashSet();
        var knownIds = lines.Select(h => h.Data.GetValueOrDefault(SonarrHistoryIdKey)).OfType<string>().ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var perEpisode in records.GroupBy(r => r.EpisodeId))
        {
            var target = targets[perEpisode.Key];
            var episode = episodes[perEpisode.Key];
            var (hadFile, quality) = before is not null && before.TryGetValue(target, out var state) ? state : (false, null);
            foreach (var line in HistoryLines(perEpisode.OrderBy(r => r.Date).ThenBy(r => r.Id), hadFile, quality))
            {
                var sourceTitle = Truncate(line.Record.SourceTitle, 1024);
                if (knownIds.Contains(line.Data[SonarrHistoryIdKey]) || !known.Add((target.Id, line.Type, line.Date.Ticks, sourceTitle ?? string.Empty)))
                {
                    continue;
                }

                line.Data["episode"] = $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
                var indexerName = Truncate(line.Data.GetValueOrDefault("indexer"), 128);
                db.History.Add(new HistoryEvent
                {
                    TargetId = target.Id,
                    SeriesId = series.Id,
                    TvdbId = series.TvdbId,
                    SeriesTitle = Truncate(series.Title, 500),
                    VersionName = version.Name,
                    EventType = line.Type,
                    SourceTitle = sourceTitle,
                    IndexerId = indexerName is not null && context.Indexers.TryGetValue(indexerName, out var indexerId) ? indexerId : null,
                    IndexerName = indexerName,
                    Data = line.Data,
                    CreatedAt = line.Date,
                });
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Sonarr's blocklist of one series, as entries of the episode targets it names. Sonarr gives no guid or info hash, so
    /// the entries match on the release title. A title an episode already blocklists is not added twice.
    /// </summary>
    private async Task<int> AddBlocklistAsync(SonarrSeries item, Dictionary<int, EpisodeTarget> targets, ImportContext context, CancellationToken ct)
    {
        var records = context.Snapshot.Blocklist[item.Id].OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
        if (records.Count == 0)
        {
            return 0;
        }

        var targetIds = targets.Values.Select(t => t.Id).ToList();
        var known = (await db.Blocklist.AsNoTracking().Where(b => targetIds.Contains(b.TargetId)).Select(b => new { b.TargetId, b.SourceTitle }).ToListAsync(ct))
            .Select(b => (b.TargetId, b.SourceTitle.ToUpperInvariant()))
            .ToHashSet();

        var added = 0;
        foreach (var record in records)
        {
            var sourceTitle = Truncate(record.SourceTitle, 1024);
            if (sourceTitle is null)
            {
                continue;
            }

            foreach (var episodeId in record.EpisodeIds ?? [])
            {
                if (!targets.TryGetValue(episodeId, out var target) || !known.Add((target.Id, sourceTitle.ToUpperInvariant())))
                {
                    continue;
                }

                var indexerName = Truncate(record.Indexer, 128);
                db.Blocklist.Add(new BlocklistEntry
                {
                    TargetId = target.Id,
                    SourceTitle = sourceTitle,
                    IndexerId = indexerName is not null && context.Indexers.TryGetValue(indexerName, out var indexerId) ? indexerId : null,
                    IndexerName = indexerName,
                    Reason = Truncate(record.Message, 1024),
                    CreatedAt = Utc(record.Date),
                });
                added++;
            }
        }

        return added;
    }

    /// <summary>A Sonarr history line translated: its Bigarr type, UTC date and the data keys the history page reads.</summary>
    public sealed record HistoryLine(SonarrHistoryItem Record, HistoryEventType Type, DateTime Date, Dictionary<string, string> Data);

    /// <summary>
    /// Sonarr's lines of one episode, oldest first, in Bigarr's terms, by the film rules: an import that follows an earlier
    /// import, with no deletion in between other than the upgrade's own, becomes <see cref="HistoryEventType.Upgraded"/>;
    /// events Bigarr has no counterpart for (<c>downloadIgnored</c>, <c>unknown</c>) are left out. A partial history (the
    /// follow's lines since its checkpoint) starts from the file the episode had: <paramref name="hadFile"/>.
    /// </summary>
    public static IEnumerable<HistoryLine> HistoryLines(IEnumerable<SonarrHistoryItem> records, bool hadFile = false, string? fileQuality = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var currentQuality = fileQuality;
        var hasFile = hadFile;
        foreach (var record in records)
        {
            if (HistoryType(record.EventType) is not { } type)
            {
                continue;
            }

            var raw = new Dictionary<string, string?>(record.Data ?? [], StringComparer.OrdinalIgnoreCase);
            string? Raw(string key) => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

            var data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["importedFrom"] = "sonarr",
                [SonarrHistoryIdKey] = record.Id.ToString(CultureInfo.InvariantCulture),
            };
            void Put(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    data[key] = value.Trim();
                }
            }

            var quality = SonarrQualities.ToBigarr(record.Quality?.Quality)?.Name;
            Put("quality", quality is "Unknown" ? null : quality);
            Put("indexer", Raw("indexer"));
            switch (type)
            {
                case HistoryEventType.Grabbed:
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    Put("size", Raw("size"));
                    Put("releaseGuid", Raw("guid"));
                    Put("releaseGroup", Raw("releaseGroup"));
                    Put("protocol", Raw("protocol"));
                    Put("releaseType", Raw("releaseType"));
                    Put("indexerFlags", Raw("indexerFlags") is { } flags && flags != "0" ? flags : null);
                    break;
                case HistoryEventType.Imported:
                    Put("path", Raw("importedPath"));
                    Put("source", Raw("droppedPath"));
                    Put("size", Raw("size"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    if (hasFile)
                    {
                        type = HistoryEventType.Upgraded;
                        Put("previousQuality", currentQuality);
                    }

                    hasFile = true;
                    currentQuality = data.GetValueOrDefault("quality");
                    break;
                case HistoryEventType.DownloadFailed:
                    Put("reason", Raw("message"));
                    Put("downloadClient", Raw("downloadClientName") ?? Raw("downloadClient"));
                    Put("downloadId", record.DownloadId);
                    break;
                case HistoryEventType.FileDeleted:
                    Put("reason", Raw("reason"));
                    Put("path", record.SourceTitle);
                    Put("size", Raw("size"));
                    if (!string.Equals(Raw("reason"), "upgrade", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFile = false;
                        currentQuality = null;
                    }

                    break;
                case HistoryEventType.Renamed:
                    Put("from", Raw("sourcePath"));
                    Put("to", Raw("path"));
                    break;
            }

            if (record.CustomFormats is { Count: > 0 } formats)
            {
                Put("customFormats", string.Join(", ", formats.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n))));
            }

            yield return new HistoryLine(record, type, Utc(record.Date), data);
        }
    }

    public static HistoryEventType? HistoryType(string? sonarrEventType) => sonarrEventType?.ToLowerInvariant() switch
    {
        "grabbed" => HistoryEventType.Grabbed,
        "downloadfolderimported" or "seriesfolderimported" => HistoryEventType.Imported,
        "downloadfailed" => HistoryEventType.DownloadFailed,
        "episodefiledeleted" => HistoryEventType.FileDeleted,
        "episodefilerenamed" => HistoryEventType.Renamed,
        _ => null,
    };

    private async Task ImportExclusionsAsync(Snapshot snapshot, SonarrImportProgress progress, CancellationToken ct)
    {
        if (snapshot.Exclusions.Count == 0)
        {
            return;
        }

        var imported = snapshot.Series.Select(s => s.TvdbId).ToHashSet();
        var candidates = snapshot.Exclusions.Where(e => e.TvdbId > 0 && !imported.Contains(e.TvdbId)).DistinctBy(e => e.TvdbId).ToList();
        var ids = candidates.Select(e => e.TvdbId).ToList();
        var inLibrary = (await db.Series.Where(s => s.TvdbId != null && ids.Contains(s.TvdbId.Value)).Select(s => s.TvdbId!.Value).ToListAsync(ct)).ToHashSet();
        var excluded = (await db.SeriesExclusions.Where(e => ids.Contains(e.TvdbId)).Select(e => e.TvdbId).ToListAsync(ct)).ToHashSet();
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var exclusion in candidates.Where(e => !inLibrary.Contains(e.TvdbId) && !excluded.Contains(e.TvdbId)))
        {
            db.SeriesExclusions.Add(new SeriesExclusion { TvdbId = exclusion.TvdbId, Title = Truncate(exclusion.Title, 512) ?? $"TVDB {exclusion.TvdbId}", CreatedAt = now });
            progress.ExclusionAdded();
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<ImportContext> ContextAsync(SonarrImportRequest request, SonarrConnection connection, Snapshot snapshot, SonarrImportProgress progress, CancellationToken ct)
    {
        var versionName = request.VersionName.Trim();
        var profileMap = request.ProfileMap ?? [];
        var knownProfiles = (await db.QualityProfiles.AsNoTracking().Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var profiles = profileMap.Where(kv => knownProfiles.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var rootRows = await db.RootFolders.AsNoTracking().ToListAsync(ct);
        var roots = new Dictionary<string, RootFolder>(StringComparer.Ordinal);
        foreach (var (sonarrPath, rootId) in request.RootFolderMap ?? [])
        {
            if (rootRows.FirstOrDefault(r => r.Id == rootId && r.Kind == MediaKind.Series) is { } root)
            {
                roots[RadarrImportService.NormalizePath(sonarrPath)] = root;
            }
        }

        var qualityIds = (await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct)).ToHashSet();

        // Sonarr names the indexer of a grab; the same indexer synced from Prowlarr carries the same name here.
        var indexers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var indexer in await db.Indexers.AsNoTracking().Select(i => new { i.Id, i.Name }).ToListAsync(ct))
        {
            indexers.TryAdd(indexer.Name, indexer.Id);
        }

        var tagMap = new Dictionary<int, int>();
        if (request.ImportTags)
        {
            var used = snapshot.Series.SelectMany(s => s.Tags ?? []).ToHashSet();
            foreach (var sonarrTag in snapshot.Tags.Where(t => used.Contains(t.Id)))
            {
                var label = TagService.NormalizeLabel(sonarrTag.Label);
                if (label.Length == 0 || label.Length > TagService.MaxLabelLength)
                {
                    continue;
                }

                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Label == label, ct);
                if (tag is null)
                {
                    tag = new Tag { Label = label };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync(ct);
                    progress.TagAdded();
                }

                tagMap[sonarrTag.Id] = tag.Id;
            }

            db.ChangeTracker.Clear();
        }

        return new ImportContext(request, connection, snapshot, versionName, profiles, roots, tagMap, qualityIds, indexers, rootRows.ToDictionary(r => r.Id, r => r.Path));
    }

    private async Task<List<Tag>> TagsForAsync(List<int>? sonarrTagIds, ImportContext context, CancellationToken ct)
    {
        var ids = (sonarrTagIds ?? []).Select(id => context.Tags.TryGetValue(id, out var tagId) ? tagId : 0).Where(id => id > 0).Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Tags.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
    }

    private async Task<Snapshot> LoadAsync(SonarrConnection connection, bool withHistory, bool withBlocklist, CancellationToken ct)
    {
        var status = await sonarr.StatusAsync(connection, ct);
        var profiles = await sonarr.ProfilesAsync(connection, ct);
        var roots = await sonarr.RootFoldersAsync(connection, ct);
        var tags = await sonarr.TagsAsync(connection, ct);
        var series = await sonarr.SeriesAsync(connection, ct);
        var exclusions = await sonarr.ExclusionsAsync(connection, ct);
        var history = withHistory ? await sonarr.HistoryAsync(connection, ct) : [];
        var blocklist = withBlocklist ? await sonarr.BlocklistAsync(connection, ct) : [];
        return new Snapshot(
            status, profiles, roots, tags, series.Where(s => s.TvdbId > 0 && !string.IsNullOrWhiteSpace(s.Title)).ToList(), exclusions,
            history.ToLookup(h => h.SeriesId), blocklist.ToLookup(b => b.SeriesId));
    }

    public static SeriesType SeriesTypeOf(string? value) => value?.ToLowerInvariant() switch
    {
        "anime" => SeriesType.Anime,
        "daily" => SeriesType.Daily,
        _ => SeriesType.Standard,
    };

    private static string TypeName(SeriesType type) => type switch
    {
        SeriesType.Anime => "anime",
        SeriesType.Daily => "daily",
        _ => "standard",
    };

    public static SeriesStatus Status(string? value, bool? ended) => value?.ToLowerInvariant() switch
    {
        "ended" => SeriesStatus.Ended,
        "upcoming" => SeriesStatus.Upcoming,
        "deleted" => SeriesStatus.Deleted,
        "continuing" => SeriesStatus.Continuing,
        _ => ended == true ? SeriesStatus.Ended : SeriesStatus.Continuing,
    };

    /// <summary>Sonarr v4's <c>monitorNewItems</c>; Sonarr v3 has none and monitors new seasons.</summary>
    public static NewItemMonitoring NewItems(string? value) =>
        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? NewItemMonitoring.None : NewItemMonitoring.All;

    private static bool LooksUhd(string name) =>
        name.Contains("2160", StringComparison.OrdinalIgnoreCase) || name.Contains("4k", StringComparison.OrdinalIgnoreCase) || name.Contains("uhd", StringComparison.OrdinalIgnoreCase) || name.Contains("ultra", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Sonarr root a series sits in, as Sonarr reports it or as its path's parent; normalized for the mapping.</summary>
    public static string? RootPathOf(SonarrSeries item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!string.IsNullOrWhiteSpace(item.RootFolderPath))
        {
            return RadarrImportService.NormalizePath(item.RootFolderPath);
        }

        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return null;
        }

        var path = item.Path.TrimEnd(Separators);
        var cut = path.LastIndexOfAny(Separators);
        return cut <= 0 ? null : RadarrImportService.NormalizePath(path[..cut]);
    }

    /// <summary>Sonarr's file path below the series folder (season folder included), with this host's separators.</summary>
    public static string? RelativePathOf(SonarrEpisodeFile file, SonarrSeries item)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(item);

        var relative = NullIfBlank(file.RelativePath) ?? (string.IsNullOrWhiteSpace(file.Path) ? null : RadarrImportService.RelativeTo(file.Path, item.Path));
        return relative?.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
    }

    private async Task SkipAsync(SonarrImportProgress progress, SonarrSeries item, string reason, CancellationToken ct)
    {
        progress.Skipped();
        progress.Issue(item.Title, item.TvdbId, reason, await SeriesIdAsync(item.TvdbId, ct));
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SonarrConnection? Connection(string? url, string? apiKey) =>
        string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) ? null : new SonarrConnection(url.Trim(), apiKey.Trim());

    /// <param name="History">Sonarr's history by Sonarr series id; empty when the import does not copy it.</param>
    /// <param name="Blocklist">Sonarr's blocklist by Sonarr series id; empty when the import does not copy it.</param>
    private sealed record Snapshot(
        RadarrSystemStatus Status,
        List<RadarrProfile> Profiles,
        List<RadarrRootFolder> RootFolders,
        List<RadarrTag> Tags,
        List<SonarrSeries> Series,
        List<SonarrExclusion> Exclusions,
        ILookup<int, SonarrHistoryItem> History,
        ILookup<int, SonarrBlocklistItem> Blocklist);

    /// <param name="Profiles">Sonarr profile id → Bigarr profile id.</param>
    /// <param name="Roots">Normalized Sonarr root path → Bigarr series root folder.</param>
    /// <param name="Tags">Sonarr tag id → Bigarr tag id.</param>
    /// <param name="Indexers">Bigarr indexer name → id, to link a grab or a blocklisted release to the indexer that still exists.</param>
    /// <param name="RootPaths">Bigarr root folder id → path: an existing version's files live under its own root.</param>
    /// <param name="Follow">A pass of the follow rather than the import: files and folders follow Sonarr's.</param>
    /// <param name="SkipFiles">The follow's first step: series come in without their files, which the next step brings.</param>
    private sealed record ImportContext(
        SonarrImportRequest Request,
        SonarrConnection Connection,
        Snapshot Snapshot,
        string VersionName,
        Dictionary<int, int> Profiles,
        Dictionary<string, RootFolder> Roots,
        Dictionary<int, int> Tags,
        HashSet<int> QualityIds,
        Dictionary<string, int> Indexers,
        Dictionary<int, string> RootPaths,
        bool Follow = false,
        bool SkipFiles = false);
}
