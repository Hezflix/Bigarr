namespace Bigarr.Api.Features.Series;

using System.Globalization;
using System.Text.RegularExpressions;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Xem;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Library;
using Bigarr.Domain.Common;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tags;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Application rules of the Series slice: lookup, add, versions, monitoring, refresh, delete. The disk is touched only
/// by a delete asked with <c>deleteFiles</c> (through the recycle bin); the scan and the import live elsewhere.
/// </summary>
public sealed partial class SeriesService(BigarrDbContext db, ISeriesMetadataProvider metadata, XemService xem, Bigarr.Api.Infrastructure.Files.RecycleBin recycleBin, TimeProvider time, ILogger<SeriesService> logger)
{
    public DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>The series with its versions and seasons; episodes are loaded apart (they can number thousands).</summary>
    public IQueryable<Series> WithGraph(bool tracking = false)
    {
        var query = db.Series
            .Include(s => s.Seasons)
            .Include(s => s.Versions).ThenInclude(v => v.QualityProfile)
            .Include(s => s.Versions).ThenInclude(v => v.RootFolder)
            .Include(s => s.Versions).ThenInclude(v => v.Tags)
            .Include(s => s.Versions).ThenInclude(v => v.Seasons)
            .AsSplitQuery();
        return tracking ? query : query.AsNoTracking();
    }

    // ---- lookup -----------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<SeriesLookupResult>>> LookupAsync(string term, CancellationToken ct)
    {
        IReadOnlyList<SeriesMetadata> found;
        try
        {
            found = await FindAsync(term.Trim(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Series metadata provider failed for lookup '{Term}'", term);
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        var tvdbIds = found.Select(f => f.TvdbId).OfType<int>().ToList();
        var tmdbIds = found.Select(f => f.TmdbId).OfType<int>().ToList();
        var existing = await db.Series
            .Where(s => (s.TvdbId != null && tvdbIds.Contains(s.TvdbId.Value)) || (s.TmdbId != null && tmdbIds.Contains(s.TmdbId.Value)))
            .Select(s => new { s.Id, s.TvdbId, s.TmdbId })
            .ToListAsync(ct);

        return Result<IReadOnlyList<SeriesLookupResult>>.Ok(found.Select(f => new SeriesLookupResult(
            f.TvdbId,
            f.TmdbId,
            f.ImdbId,
            f.Title,
            f.OriginalTitle,
            f.Year,
            f.Overview,
            f.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            f.Status,
            f.Network,
            f.SuggestedType,
            existing.FirstOrDefault(e => (f.TvdbId is not null && e.TvdbId == f.TvdbId) || (f.TmdbId is not null && e.TmdbId == f.TmdbId))?.Id)).ToList());
    }

    private async Task<IReadOnlyList<SeriesMetadata>> FindAsync(string term, CancellationToken ct)
    {
        var id = IdTermRegex().Match(term);
        if (id.Success)
        {
            var value = int.Parse(id.Groups["id"].Value, CultureInfo.InvariantCulture);
            var one = id.Groups["source"].Value.Equals("tvdb", StringComparison.OrdinalIgnoreCase)
                ? await metadata.GetByTvdbIdAsync(value, withEpisodes: false, ct)
                : await metadata.GetByTmdbIdAsync(value, withEpisodes: false, ct);
            return one is null ? [] : [one];
        }

        return term.Length == 0 ? [] : await metadata.SearchAsync(term, ct);
    }

    // ---- add --------------------------------------------------------------------------------------------------------

    public async Task<Result<Series>> AddSeriesAsync(AddSeriesRequest request, CancellationToken ct)
    {
        if (request.TvdbId is null && request.TmdbId is null)
        {
            return Failure.Invalid("A TVDB or TMDB id is required");
        }

        if (await ExistingIdAsync(request.TvdbId, request.TmdbId, ct) is { } existingId)
        {
            return Failure.Conflict("Series already in the library", $"It is series {existingId}. Add a version instead.");
        }

        var profile = await db.QualityProfiles.FindAsync([request.QualityProfileId], ct);
        if (profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
        }

        if (Quality.ProfileKindRules.ForSeriesVersion(profile) is { } wrongKind)
        {
            return wrongKind;
        }

        var root = await db.RootFolders.FindAsync([request.RootFolderId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {request.RootFolderId}.");
        }

        if (root.Kind != MediaKind.Series)
        {
            return Failure.Invalid("Not a series root folder", $"'{root.Path}' holds films. Pick a series root folder or change its kind in Settings → Library.");
        }

        var versionName = string.IsNullOrWhiteSpace(request.VersionName) ? VersionNaming.FromSeriesProfile(profile.Name) : request.VersionName.Trim();
        if (!Slug.IsValid(versionName))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        var tags = await ResolveTagsAsync(request.TagIds, ct);
        if (!tags.IsSuccess)
        {
            return tags.Failure!;
        }

        SeriesMetadata? found;
        try
        {
            found = request.TvdbId is { } tvdbId
                ? await metadata.GetByTvdbIdAsync(tvdbId, withEpisodes: true, ct)
                : null;
            if (found is null && request.TmdbId is { } tmdbId)
            {
                found = await metadata.GetByTmdbIdAsync(tmdbId, withEpisodes: true, ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Series metadata provider failed for TVDB {TvdbId} / TMDB {TmdbId}", request.TvdbId, request.TmdbId);
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (found is null)
        {
            return Failure.NotFound(request.TvdbId is not null ? $"TVDB series {request.TvdbId}" : $"TMDB series {request.TmdbId}");
        }

        // Asked by TMDB id, found under a TVDB id the library already has (or the other way round).
        if (await ExistingIdAsync(found.TvdbId, found.TmdbId, ct) is { } sameShow)
        {
            return Failure.Conflict("Series already in the library", $"It is series {sameShow}. Add a version instead.");
        }

        var series = Series.FromMetadata(found, root.Id);
        series.SeriesType = request.SeriesType ?? found.SuggestedType;
        SyncCatalogue(series, found, []);

        var version = new SeriesVersion
        {
            Series = series,
            Name = versionName,
            Mode = VersionMode.Managed,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = string.IsNullOrWhiteSpace(request.FolderName) ? await FolderNameAsync(series, versionName, ct) : FolderNameBuilder.Sanitize(request.FolderName),
            IsDefault = true,
            Monitored = request.Monitored,
            SeasonFolder = request.SeasonFolder,
            MonitorNewItems = request.MonitorNewItems,
            Tags = tags.Value!,
            CreatedAt = Now,
        };
        series.Versions.Add(version);
        AttachVersion(series, version, request.Monitor);
        series.AddedAt = Now;
        await xem.RefreshSeriesAsync(series, series.Episodes.ToList(), ct);

        db.Series.Add(series);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE constraint failed: Series.", StringComparison.Ordinal) == true)
        {
            return Failure.Conflict("Series already in the library", $"{series.Title} was added concurrently.");
        }

        logger.LogInformation("Added series {Title} ({Source}): {Episodes} episodes, monitor {Monitor}", series.Title, found.Source, series.Episodes.Count, request.Monitor);
        return Result<Series>.Ok(series);
    }

    private async Task<int?> ExistingIdAsync(int? tvdbId, int? tmdbId, CancellationToken ct) =>
        await db.Series
            .Where(s => (tvdbId != null && s.TvdbId == tvdbId) || (tmdbId != null && s.TmdbId == tmdbId))
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Creates the version's episode targets and season flags from a monitoring option. The episodes are those of the
    /// series graph (saved or not): the rules work on positions, not ids.
    /// </summary>
    private void AttachVersion(Series series, SeriesVersion version, MonitoringOption option)
    {
        var now = Now;
        var episodes = series.Episodes.ToList();
        var facts = episodes.Select((e, i) => new EpisodeFacts(i, e.SeasonNumber, e.EpisodeNumber, e.HasAired(now), HasFile: false)).ToList();
        var monitored = MonitoringRules.MonitoredEpisodes(option, facts);
        var seasonFlags = MonitoringRules.MonitoredSeasons(option, series.Seasons.Select(s => s.SeasonNumber).ToList(), facts, monitored);

        for (var i = 0; i < episodes.Count; i++)
        {
            version.Episodes.Add(new EpisodeTarget { Version = version, Episode = episodes[i], Monitored = monitored.Contains(i), CreatedAt = now });
        }

        foreach (var season in series.Seasons)
        {
            version.Seasons.Add(new SeriesVersionSeason { Version = version, Season = season, Monitored = seasonFlags[season.SeasonNumber] });
        }
    }

    public async Task<Result<SeriesVersion>> AddVersionAsync(int seriesId, AddSeriesVersionRequest request, CancellationToken ct)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (!Slug.IsValid(name))
        {
            return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
        }

        if (series.Versions.Any(v => v.Name == name))
        {
            return Failure.Conflict("Version name already used", $"Series {seriesId} already has a version named '{name}'.");
        }

        var profile = await db.QualityProfiles.FindAsync([request.QualityProfileId], ct);
        if (profile is null)
        {
            return Failure.Invalid("Unknown quality profile", $"No quality profile with id {request.QualityProfileId}.");
        }

        if (Quality.ProfileKindRules.ForSeriesVersion(profile) is { } wrongKind)
        {
            return wrongKind;
        }

        var rootId = request.RootFolderId ?? series.DefaultRootFolderId;
        var root = await db.RootFolders.FindAsync([rootId], ct);
        if (root is null)
        {
            return Failure.Invalid("Unknown root folder", $"No root folder with id {rootId}.");
        }

        if (root.Kind != MediaKind.Series)
        {
            return Failure.Invalid("Not a series root folder", $"'{root.Path}' holds films.");
        }

        var tags = await ResolveTagsAsync(request.TagIds, ct);
        if (!tags.IsSuccess)
        {
            return tags.Failure!;
        }

        var constraints = Movies.MovieMapper.FromConstraints(request.Constraints);
        if (await ValidateConstraintsAsync(constraints, ct) is { } invalidConstraints)
        {
            return invalidConstraints;
        }

        await db.Episodes.Where(e => e.SeriesId == seriesId).LoadAsync(ct);
        var defaultVersion = series.Versions.FirstOrDefault(v => v.IsDefault);
        var version = new SeriesVersion
        {
            Series = series,
            Name = name,
            Mode = VersionMode.Managed,
            QualityProfile = profile,
            RootFolder = root,
            FolderName = string.IsNullOrWhiteSpace(request.FolderName)
                ? await FolderNameAsync(series, name, ct)
                : FolderNameBuilder.Sanitize(request.FolderName),
            IsDefault = defaultVersion is null,
            Monitored = request.Monitored,
            SeasonFolder = request.SeasonFolder,
            MonitorNewItems = request.MonitorNewItems,
            Tags = tags.Value!,
            Constraints = constraints,
            CreatedAt = Now,
        };
        series.Versions.Add(version);
        AttachVersion(series, version, request.Monitor);
        await db.SaveChangesAsync(ct);
        return Result<SeriesVersion>.Ok(version);
    }

    // ---- edit -------------------------------------------------------------------------------------------------------

    public async Task<Result<Series>> UpdateSeriesAsync(int seriesId, UpdateSeriesRequest request, CancellationToken ct)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        if (request.SeriesType is { } type)
        {
            series.SeriesType = type;
        }

        await db.SaveChangesAsync(ct);
        return Result<Series>.Ok(series);
    }

    public async Task<Result<SeriesVersion>> UpdateVersionAsync(int seriesId, int versionId, UpdateSeriesVersionRequest request, CancellationToken ct)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        var version = series?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (series is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (!Slug.IsValid(name))
            {
                return Failure.Invalid("Invalid version name", "A version name is a lowercase slug: letters, digits and single dashes.");
            }

            if (series.Versions.Any(v => v.Id != versionId && v.Name == name))
            {
                return Failure.Conflict("Version name already used", $"Series {seriesId} already has a version named '{name}'.");
            }

            version.Name = name;
        }

        if (request.QualityProfileId is { } profileId && profileId != version.QualityProfileId)
        {
            var profile = await db.QualityProfiles.FindAsync([profileId], ct);
            if (profile is null)
            {
                return Failure.Invalid("Unknown quality profile", $"No quality profile with id {profileId}.");
            }

            if (Quality.ProfileKindRules.ForSeriesVersion(profile) is { } wrongKind)
            {
                return wrongKind;
            }

            version.QualityProfile = profile;
        }

        version.Monitored = request.Monitored ?? version.Monitored;
        version.SeasonFolder = request.SeasonFolder ?? version.SeasonFolder;
        version.MonitorNewItems = request.MonitorNewItems ?? version.MonitorNewItems;

        if (request.ClearConstraints)
        {
            version.Constraints = null;
        }
        else if (request.Constraints is not null)
        {
            var constraints = Movies.MovieMapper.FromConstraints(request.Constraints);
            if (await ValidateConstraintsAsync(constraints, ct) is { } invalidConstraints)
            {
                return invalidConstraints;
            }

            version.Constraints = constraints;
        }

        if (request.TagIds is not null)
        {
            var tags = await ResolveTagsAsync(request.TagIds, ct);
            if (!tags.IsSuccess)
            {
                return tags.Failure!;
            }

            version.Tags.Clear();
            foreach (var tag in tags.Value!)
            {
                version.Tags.Add(tag);
            }
        }

        await db.SaveChangesAsync(ct);
        return Result<SeriesVersion>.Ok(version);
    }

    /// <summary>Like Sonarr's season toggle: the season flag and every episode of the season, for this version only.</summary>
    public async Task<Result<bool>> SetSeasonMonitoredAsync(int seriesId, int versionId, int seasonNumber, bool monitored, CancellationToken ct)
    {
        var flag = await db.SeriesVersionSeasons
            .FirstOrDefaultAsync(s => s.VersionId == versionId && s.Version.SeriesId == seriesId && s.Season.SeasonNumber == seasonNumber, ct);
        if (flag is null)
        {
            return Failure.NotFound("Season");
        }

        flag.Monitored = monitored;
        var targets = await db.EpisodeTargets.Where(t => t.VersionId == versionId && t.Episode.SeasonNumber == seasonNumber).ToListAsync(ct);
        foreach (var target in targets)
        {
            target.Monitored = monitored;
        }

        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(monitored);
    }

    /// <summary>Sonarr's <c>PUT /episode/monitor</c>, for one version. Unknown episode ids are a 400, not a partial write.</summary>
    public async Task<Result<int>> SetEpisodesMonitoredAsync(int seriesId, int versionId, IReadOnlyCollection<int> episodeIds, bool monitored, CancellationToken ct)
    {
        if (!await db.SeriesVersions.AnyAsync(v => v.Id == versionId && v.SeriesId == seriesId, ct))
        {
            return Failure.NotFound("Version");
        }

        var ids = episodeIds.Distinct().ToList();
        var targets = await db.EpisodeTargets.Where(t => t.VersionId == versionId && ids.Contains(t.EpisodeId)).ToListAsync(ct);
        if (targets.Count != ids.Count)
        {
            var unknown = ids.Except(targets.Select(t => t.EpisodeId));
            return Failure.Invalid("Unknown episode", $"Series {seriesId} has no episode with id {string.Join(", ", unknown)}.");
        }

        foreach (var target in targets)
        {
            target.Monitored = monitored;
        }

        await db.SaveChangesAsync(ct);
        return Result<int>.Ok(targets.Count);
    }

    /// <summary>Reapplies a monitoring option to a version, as Sonarr's series edit does (episode and season flags rewritten).</summary>
    public async Task<Result<bool>> ApplyMonitoringAsync(int seriesId, int versionId, MonitoringOption option, CancellationToken ct)
    {
        var version = await db.SeriesVersions
            .Include(v => v.Episodes).ThenInclude(t => t.Episode)
            .Include(v => v.Seasons).ThenInclude(s => s.Season)
            .AsSplitQuery()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.SeriesId == seriesId, ct);
        if (version is null)
        {
            return Failure.NotFound("Version");
        }

        var now = Now;
        var facts = version.Episodes
            .Select(t => new EpisodeFacts(t.EpisodeId, t.Episode.SeasonNumber, t.Episode.EpisodeNumber, t.Episode.HasAired(now), t.HasFile))
            .ToList();
        var monitored = MonitoringRules.MonitoredEpisodes(option, facts);
        var seasons = MonitoringRules.MonitoredSeasons(option, version.Seasons.Select(s => s.Season.SeasonNumber).ToList(), facts, monitored);

        foreach (var target in version.Episodes)
        {
            target.Monitored = monitored.Contains(target.EpisodeId);
        }

        foreach (var season in version.Seasons)
        {
            season.Monitored = seasons[season.Season.SeasonNumber];
        }

        await db.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    // ---- refresh ----------------------------------------------------------------------------------------------------

    /// <summary>Targets among <paramref name="targetIds"/> that a grab, a shadow pick or a history event points at, with what it is.</summary>
    private async Task<Dictionary<int, string>> RecordedTargetsAsync(List<int> targetIds, CancellationToken ct)
    {
        var recorded = new Dictionary<int, string>();
        if (targetIds.Count == 0)
        {
            return recorded;
        }

        foreach (var id in await db.GrabTargets.Where(t => targetIds.Contains(t.TargetId)).Select(t => t.TargetId).Distinct().ToListAsync(ct))
        {
            recorded.TryAdd(id, "a grab");
        }

        foreach (var id in await db.SeriesShadowPickEpisodes.Where(p => p.TargetId != null && targetIds.Contains(p.TargetId.Value)).Select(p => p.TargetId!.Value).Distinct().ToListAsync(ct))
        {
            recorded.TryAdd(id, "a shadow pick");
        }

        foreach (var id in await db.History.Where(h => h.TargetId != null && targetIds.Contains(h.TargetId.Value)).Select(h => h.TargetId!.Value).Distinct().ToListAsync(ct))
        {
            recorded.TryAdd(id, "history");
        }

        return recorded;
    }

    /// <summary>Same rules as a film version's constraints: no format both required and forbidden, every format known.</summary>
    private async Task<Failure?> ValidateConstraintsAsync(AcquisitionConstraints? constraints, CancellationToken ct)
    {
        if (constraints is null)
        {
            return null;
        }

        if (constraints.RequiredCustomFormatIds.Intersect(constraints.ForbiddenCustomFormatIds).Any())
        {
            return Failure.Invalid("Contradictory constraints", "A custom format cannot be both required and forbidden.");
        }

        var ids = constraints.RequiredCustomFormatIds.Concat(constraints.ForbiddenCustomFormatIds).Distinct().ToList();
        if (ids.Count == 0)
        {
            return null;
        }

        var known = await db.CustomFormats.Where(f => ids.Contains(f.Id)).Select(f => f.Id).ToListAsync(ct);
        var unknown = ids.Except(known).ToList();
        return unknown.Count == 0 ? null : Failure.Invalid("Unknown custom format", $"No custom format with id {string.Join(", ", unknown)}.");
    }

    public async Task<Result<Series>> RefreshAsync(int seriesId, CancellationToken ct)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        SeriesMetadata? found;
        try
        {
            found = series.TvdbId is { } tvdbId ? await metadata.GetByTvdbIdAsync(tvdbId, withEpisodes: true, ct) : null;
            if (found is null && series.TmdbId is { } tmdbId)
            {
                found = await metadata.GetByTmdbIdAsync(tmdbId, withEpisodes: true, ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return new Failure(StatusCodes.Status502BadGateway, "Metadata provider unavailable", ex.Message);
        }

        if (found is null)
        {
            series.Status = SeriesStatus.Deleted;
            series.LastMetadataRefreshAt = Now;
            await db.SaveChangesAsync(ct);
            return Result<Series>.Ok(series);
        }

        // Tracked loads: the context links episodes, targets and season flags to the series graph.
        await db.Episodes.Where(e => e.SeriesId == seriesId).Include(e => e.Targets).LoadAsync(ct);

        series.ApplyMetadata(found);
        series.LastMetadataRefreshAt = Now;
        var sync = SyncCatalogue(series, found, series.Episodes.ToList());
        await xem.RefreshSeriesAsync(series, series.Episodes.ToList(), ct);

        // Both numberings stay current: the alternative order is re-read and re-linked (files are not renamed here).
        await SeriesOrderService.RelinkAsync(metadata, logger, series, series.Episodes.Where(e => !sync.Removed.Contains(e)).ToList(), ct);

        foreach (var version in series.Versions)
        {
            foreach (var season in sync.NewSeasons)
            {
                version.Seasons.Add(new SeriesVersionSeason { Version = version, Season = season, Monitored = MonitoringRules.NewSeasonMonitored(version, season.SeasonNumber) });
            }

            foreach (var episode in sync.Added)
            {
                var seasonMonitored = version.Seasons.FirstOrDefault(s => s.Season.SeasonNumber == episode.SeasonNumber)?.Monitored ?? false;
                version.Episodes.Add(new EpisodeTarget { Version = version, Episode = episode, Monitored = episode.SeasonNumber > 0 && seasonMonitored, CreatedAt = Now });
            }
        }

        // Episodes the provider dropped go, unless a version holds a file for them, or one of their targets was grabbed
        // (or picked in shadow mode) or has history: deleting the target would cut that record loose (the user decides then).
        var removedTargetIds = sync.Removed.SelectMany(e => e.Targets).Select(t => t.Id).Where(id => id > 0).ToList();
        var recorded = await RecordedTargetsAsync(removedTargetIds, ct);
        foreach (var episode in sync.Removed)
        {
            if (episode.Targets.Any(t => t.EpisodeFileId is not null))
            {
                logger.LogWarning("{Series}: S{Season:00}E{Episode:00} is gone from the provider but has a file; kept", series.Title, episode.SeasonNumber, episode.EpisodeNumber);
                continue;
            }

            if (episode.Targets.FirstOrDefault(t => recorded.ContainsKey(t.Id)) is { } kept)
            {
                logger.LogWarning("{Series}: S{Season:00}E{Episode:00} is gone from the provider but has {What}; kept", series.Title, episode.SeasonNumber, episode.EpisodeNumber, recorded[kept.Id]);
                continue;
            }

            db.EpisodeTargets.RemoveRange(episode.Targets);
            db.Episodes.Remove(episode);
        }

        var keptSeasons = series.Episodes.Where(e => db.Entry(e).State != EntityState.Deleted).Select(e => e.SeasonNumber).Concat(found.Seasons.Select(s => s.SeasonNumber)).ToHashSet();
        foreach (var season in series.Seasons.Where(s => !keptSeasons.Contains(s.SeasonNumber)).ToList())
        {
            db.Seasons.Remove(season);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Refreshed series {Title}: {Added} new episodes, {Removed} gone", series.Title, sync.Added.Count, sync.Removed.Count);
        return Result<Series>.Ok(series);
    }

    private sealed record CatalogueChanges(List<Season> NewSeasons, List<Episode> Added, List<Episode> Removed);

    /// <summary>
    /// Brings seasons and episodes in line with the provider. An episode keeps its row (and every version's state) as
    /// long as its TVDB id is listed, whatever its number becomes; without TVDB ids the TMDB id, then the number, match.
    /// </summary>
    private CatalogueChanges SyncCatalogue(Series series, SeriesMetadata found, IReadOnlyCollection<Episode> existing)
    {
        var seasons = series.Seasons.ToDictionary(s => s.SeasonNumber);
        var newSeasons = new List<Season>();
        foreach (var number in found.Seasons.Select(s => s.SeasonNumber).Concat(found.Episodes.Select(e => e.SeasonNumber)).Distinct().Order())
        {
            if (!seasons.TryGetValue(number, out var season))
            {
                season = new Season { Series = series, SeasonNumber = number };
                series.Seasons.Add(season);
                seasons[number] = season;
                newSeasons.Add(season);
            }

            var info = found.Seasons.FirstOrDefault(s => s.SeasonNumber == number);
            season.Title = info?.Title;
            season.Overview = info?.Overview;
            season.Images = info?.PosterUrl is { } poster ? [new MediaImage(ImageType.Poster, poster)] : [];
        }

        var byTvdb = existing.Where(e => e.TvdbId is not null).GroupBy(e => e.TvdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        var byTmdb = existing.Where(e => e.TmdbId is not null).GroupBy(e => e.TmdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        var byNumber = existing.Where(e => e.TvdbId is null).GroupBy(e => (e.SeasonNumber, e.EpisodeNumber)).ToDictionary(g => g.Key, g => g.First());
        var matched = new HashSet<Episode>(ReferenceEqualityComparer.Instance);
        var added = new List<Episode>();

        foreach (var info in found.Episodes)
        {
            Episode? episode = null;
            if (info.TvdbId is { } tvdbId && byTvdb.TryGetValue(tvdbId, out var a))
            {
                episode = a;
            }
            else if (info.TmdbId is { } tmdbId && byTmdb.TryGetValue(tmdbId, out var b) && (b.TvdbId is null || info.TvdbId is null))
            {
                episode = b;
            }
            else if (info.TvdbId is null && byNumber.TryGetValue((info.SeasonNumber, info.EpisodeNumber), out var c))
            {
                episode = c;
            }

            if (episode is null || matched.Contains(episode))
            {
                episode = new Episode { Series = series, Title = info.Title };
                series.Episodes.Add(episode);
                added.Add(episode);
            }

            matched.Add(episode);
            episode.Season = seasons[info.SeasonNumber];
            episode.TvdbId = info.TvdbId;
            episode.TmdbId = info.TmdbId;
            episode.SeasonNumber = info.SeasonNumber;
            episode.EpisodeNumber = info.EpisodeNumber;
            episode.AbsoluteEpisodeNumber = info.AbsoluteEpisodeNumber;
            episode.AirDate = info.AirDate;
            episode.AirDateUtc = AirTimes.ToUtc(info.AirDate, series.AirTime, series.OriginalCountry);
            episode.Title = info.Title;
            episode.Overview = info.Overview;
            episode.Runtime = info.Runtime;
            episode.ImageUrl = info.ImageUrl;
        }

        var removed = existing.Where(e => !matched.Contains(e)).ToList();
        return new CatalogueChanges(newSeasons, added, removed);
    }

    // ---- default & delete -------------------------------------------------------------------------------------------

    public async Task<Result<SeriesVersion>> PromoteAsync(int seriesId, int versionId, CancellationToken ct)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        var version = series?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (series is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        if (version.Mode != VersionMode.Managed)
        {
            return Failure.Conflict("Only a managed version can be the default");
        }

        if (!version.IsDefault)
        {
            await SwitchDefaultAsync(series.Versions.FirstOrDefault(v => v.IsDefault), version, ct);
        }

        return Result<SeriesVersion>.Ok(version);
    }

    /// <summary>The series folder template (Settings → Media management) applied to a new version; frozen afterwards.</summary>
    private async Task<string> FolderNameAsync(Series series, string versionName, CancellationToken ct)
    {
        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        return Import.SeriesNaming.FolderName(config, series, versionName);
    }

    /// <summary>
    /// Same rules as a film's versions: the default needs a successor (<c>?promote=</c>), the last one takes the series
    /// with it. With <paramref name="deleteFiles"/> the version's files go to the recycle bin, and its folder too when no
    /// other version shares it (Radarr's rules for films). True when the series went.
    /// </summary>
    public async Task<Result<bool>> DeleteVersionAsync(int seriesId, int versionId, int? promote, CancellationToken ct, bool deleteFiles = false)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        var version = series?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (series is null || version is null)
        {
            return Failure.NotFound("Version");
        }

        var siblings = series.Versions.Where(v => v.Id != versionId).ToList();
        if (siblings.Count == 0)
        {
            if (deleteFiles && await RemoveFoldersAsync(series, [version], ct) is { } failed)
            {
                return failed;
            }

            await RemoveSeriesAsync(series, ct);
            return Result<bool>.Ok(true);
        }

        SeriesVersion? successor = null;
        if (version.IsDefault)
        {
            successor = promote is null ? null : siblings.FirstOrDefault(v => v.Id == promote);
            if (successor is null)
            {
                return Failure.Conflict("This is the default version", "Choose the new default among the other versions with ?promote=<versionId>.");
            }

            if (successor.Mode != VersionMode.Managed)
            {
                return Failure.Invalid("Only a managed version can become the default");
            }
        }

        if (deleteFiles && await RemoveVersionFilesAsync(series, version, siblings, ct) is { } removal)
        {
            return removal;
        }

        if (successor is not null)
        {
            await SwitchDefaultAsync(version, successor, ct);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DeleteTargetsAsync(versionId, null, ct);
        db.SeriesVersions.Remove(version);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result<bool>.Ok(false);
    }

    /// <summary>With <paramref name="deleteFiles"/>, every folder of the series goes to the recycle bin, like Sonarr's "delete series folder".</summary>
    public async Task<Result<bool>> DeleteSeriesAsync(int seriesId, CancellationToken ct, bool deleteFiles = false)
    {
        var series = await WithGraph(tracking: true).FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return Failure.NotFound("Series");
        }

        if (deleteFiles && await RemoveFoldersAsync(series, series.Versions.ToList(), ct) is { } failed)
        {
            return failed;
        }

        await RemoveSeriesAsync(series, ct);
        return Result<bool>.Ok(true);
    }

    /// <summary>The folders of <paramref name="versions"/> to the recycle bin (each once), with a file-deleted line per episode.</summary>
    private async Task<Failure?> RemoveFoldersAsync(Series series, List<SeriesVersion> versions, CancellationToken ct)
    {
        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        await JournalDeletedFilesAsync(series, versions, ct);
        foreach (var group in versions.GroupBy(v => (v.RootFolderId, v.FolderName)))
        {
            var root = await db.RootFolders.AsNoTracking().FirstAsync(r => r.Id == group.Key.RootFolderId, ct);
            var folder = Path.Combine(root.Path, group.Key.FolderName);
            try
            {
                recycleBin.RemoveDirectory(folder, config.RecycleBinPath, root.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not delete folder {Folder}", folder);
                return new Failure(StatusCodes.Status500InternalServerError, "Could not delete the series folder", $"{folder}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// One version's files (and extras) to the recycle bin. A folder no other version uses goes whole, as Radarr does
    /// for a film version; a shared folder keeps the other versions' files.
    /// </summary>
    private async Task<Failure?> RemoveVersionFilesAsync(Series series, SeriesVersion version, List<SeriesVersion> siblings, CancellationToken ct)
    {
        if (!siblings.Any(v => v.RootFolderId == version.RootFolderId && v.FolderName == version.FolderName))
        {
            return await RemoveFoldersAsync(series, [version], ct);
        }

        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        await JournalDeletedFilesAsync(series, [version], ct);
        var root = await db.RootFolders.AsNoTracking().FirstAsync(r => r.Id == version.RootFolderId, ct);
        var folder = Path.Combine(root.Path, version.FolderName);
        var files = await db.EpisodeFiles.AsNoTracking().Include(f => f.Extras).Where(f => f.VersionId == version.Id).ToListAsync(ct);
        foreach (var path in files.SelectMany(f => new[] { f.RelativePath }.Concat(f.Extras.Select(e => e.RelativePath))))
        {
            try
            {
                recycleBin.RemoveFile(Path.Combine(folder, path), config.RecycleBinPath, root.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new Failure(StatusCodes.Status500InternalServerError, "Could not delete a file of the version", $"{path}: {ex.Message}");
            }
        }

        return null;
    }

    private async Task JournalDeletedFilesAsync(Series series, List<SeriesVersion> versions, CancellationToken ct)
    {
        var ids = versions.Select(v => v.Id).ToList();
        var targets = await db.EpisodeTargets.AsNoTracking()
            .Include(t => t.Episode)
            .Include(t => t.EpisodeFile)
            .Where(t => ids.Contains(t.VersionId) && t.EpisodeFileId != null)
            .ToListAsync(ct);
        foreach (var target in targets)
        {
            var version = versions.First(v => v.Id == target.VersionId);
            var line = Downloads.GrabService.SeriesHistory(version, target, Bigarr.Domain.History.HistoryEventType.FileDeleted, target.EpisodeFile!.OriginalReleaseTitle, null, null, Now);
            line.TargetId = null; // the target goes with the version; the snapshots keep the line meaningful
            line.Data["path"] = Path.Combine(version.FolderName, target.EpisodeFile.RelativePath);
            line.Data["reason"] = "versionDeleted";
            db.History.Add(line);
        }
    }

    private async Task RemoveSeriesAsync(Series series, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await DeleteTargetsAsync(null, series.Id, ct);
        db.Series.Remove(series);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Episode targets are TPT children: a database cascade from the version or the series would only clear
    /// <c>EpisodeTargets</c> and leave orphans in <c>AcquisitionTargets</c>. Deleting the base rows cascades the other
    /// way (episode targets, grab links, blocklist; history keeps its snapshot with a null target).
    /// </summary>
    private Task<int> DeleteTargetsAsync(int? versionId, int? seriesId, CancellationToken ct) =>
        versionId is { } version
            ? db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM AcquisitionTargets WHERE Id IN (SELECT Id FROM EpisodeTargets WHERE VersionId = {version})", ct)
            : db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM AcquisitionTargets WHERE Id IN (SELECT t.Id FROM EpisodeTargets t JOIN SeriesVersions v ON v.Id = t.VersionId WHERE v.SeriesId = {seriesId})", ct);

    /// <summary>Two statements in one transaction: the partial unique index forbids two defaults, even transiently.</summary>
    private async Task SwitchDefaultAsync(SeriesVersion? previous, SeriesVersion next, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (previous is not null)
        {
            previous.IsDefault = false;
            await db.SaveChangesAsync(ct);
        }

        next.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Result<List<Tag>>> ResolveTagsAsync(List<int>? ids, CancellationToken ct)
    {
        var wanted = (ids ?? []).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return Result<List<Tag>>.Ok([]);
        }

        var tags = await db.Tags.Where(t => wanted.Contains(t.Id)).ToListAsync(ct);
        var unknown = wanted.Except(tags.Select(t => t.Id)).ToList();
        return unknown.Count == 0 ? Result<List<Tag>>.Ok(tags) : Failure.Invalid("Unknown tag", $"No tag with id {string.Join(", ", unknown)}.");
    }

    [GeneratedRegex(@"^(?<source>tvdb|tmdb):\s*(?<id>\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex IdTermRegex();
}
