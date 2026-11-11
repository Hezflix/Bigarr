namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes.Model;
using Bigarr.Parsing.Qualities;
using Microsoft.EntityFrameworkCore;
using EpisodeParser = Bigarr.Parsing.Episodes.Parser;

/// <summary>
/// Read-only scan of an existing series library (blueprint §5, Sonarr's library import + disk scan): one first-level
/// folder of a series root = one series version. A folder is matched to a series of the library (its version folder,
/// a TVDB/TMDB id tag, its title and year) or looked up and added unmonitored; folders that match nothing are listed
/// as unmapped. Inside a series folder every video file is parsed and attached to the episodes it holds; a file that
/// cannot be placed is listed as unmatched for the series. Nothing on disk is ever moved, renamed or deleted.
/// </summary>
public sealed class SeriesLibraryScanner(
    BigarrDbContext db,
    SeriesService seriesService,
    ISeriesMetadataProvider metadata,
    TimeProvider time,
    ILogger<SeriesLibraryScanner> logger) : IRootFolderScanner
{
    // Sonarr's ExcludedExtrasSubFolderRegex and ExcludedSubFoldersRegex. "Specials" is season 0, not an extra.
    private static readonly HashSet<string> ExtrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "extras", "extrafanart", "behind the scenes", "deleted scenes", "featurettes", "interviews", "other", "scenes", "samples", "sample",
        "shorts", "trailers", "@eadir", "plex versions", "metadata", ".@__thumb",
    };

    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "@eadir", "#recycle", "lost+found", ".recycle", "$recycle.bin", "system volume information",
    };

    public MediaKind Kind => MediaKind.Series;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ---- whole root -------------------------------------------------------------------------------------------------

    public async Task ScanAsync(int rootFolderId, ScanProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var root = await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rootFolderId, ct)
                   ?? throw new InvalidOperationException($"Root folder {rootFolderId} no longer exists.");

        if (root.Kind != MediaKind.Series)
        {
            throw new InvalidOperationException($"'{root.Path}' is a film root folder; the series scan does not walk it.");
        }

        if (!Directory.Exists(root.Path))
        {
            throw new DirectoryNotFoundException($"Root folder path '{root.Path}' is not accessible.");
        }

        var folders = Directory.EnumerateDirectories(root.Path)
            .Select(p => new DirectoryInfo(p))
            .Where(d => !d.Name.StartsWith('.') && !IgnoredFolders.Contains(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress.FoldersTotal = folders.Count;
        logger.LogInformation("Scanning series root {Root}: {Count} folder(s)", root.Path, folders.Count);

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            progress.CurrentFolder = folder.Name;

            try
            {
                await ScanFolderAsync(root, folder, progress, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan of series folder {Folder} failed", folder.FullName);
                db.ChangeTracker.Clear();
                await UpsertUnmappedAsync(root.Id, folder.Name, 0, UnmappedReason.MetadataError, ex.Message, ct);
                progress.FolderUnmapped();
            }

            progress.FolderDone();
        }

        // Unmapped folders that are gone (mapped ones were cleared as they were scanned).
        var names = folders.Select(f => f.Name).ToList();
        await db.UnmappedFolders.Where(u => u.RootFolderId == root.Id && !names.Contains(u.RelativePath)).ExecuteDeleteAsync(ct);
        progress.CurrentFolder = null;
    }

    private async Task ScanFolderAsync(RootFolder root, DirectoryInfo folder, ScanProgress progress, CancellationToken ct)
    {
        var owner = await db.SeriesVersions.AsNoTracking()
            .Where(v => v.RootFolderId == root.Id && v.FolderName == folder.Name)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(ct);

        if (owner is null)
        {
            var files = EnumerateVideoFiles(folder).ToList();
            if (files.Count == 0)
            {
                await UpsertUnmappedAsync(root.Id, folder.Name, 0, UnmappedReason.NoVideoFiles, null, ct);
                progress.FolderUnmapped();
                return;
            }

            var resolved = await ResolveFolderAsync(root, folder.Name, files, progress, ct);
            if (resolved.VersionId is null)
            {
                await UpsertUnmappedAsync(root.Id, folder.Name, files.Count, resolved.Reason, resolved.Detail, ct);
                progress.FolderUnmapped();
                return;
            }

            owner = resolved.VersionId;
        }

        await ScanVersionAsync(owner.Value, progress, ct);
        await db.UnmappedFolders.Where(u => u.RootFolderId == root.Id && u.RelativePath == folder.Name).ExecuteDeleteAsync(ct);
    }

    private sealed record FolderResolution(int? VersionId, UnmappedReason Reason = default, string? Detail = null);

    /// <summary>The version that takes this folder: of a library series (adopted or new) or of a series added now.</summary>
    private async Task<FolderResolution> ResolveFolderAsync(RootFolder root, string folderName, List<FileInfo> files, ScanProgress progress, CancellationToken ct)
    {
        var info = SeriesFolderName.Parse(folderName);
        if (info.Title.Length == 0 && info.TvdbId is null && info.TmdbId is null)
        {
            return new FolderResolution(null, UnmappedReason.NotParsable, folderName);
        }

        var index = await BuildIndexAsync(ct);
        var inLibrary = info.TvdbId is { } tagged ? index.ByTvdbId(tagged) : null;
        if (inLibrary is null && info.TmdbId is { } tmdbTag)
        {
            var byTmdb = await db.Series.AsNoTracking().Where(s => s.TmdbId == tmdbTag).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
            inLibrary = byTmdb is { } sid ? index.All.First(s => s.Id == sid) : null;
        }

        if (inLibrary is null && info.TvdbId is null && info.TmdbId is null)
        {
            var match = index.Find(info.Title, info.Title, info.Year);
            if (match.IsAmbiguous)
            {
                return new FolderResolution(null, UnmappedReason.AmbiguousMatch, string.Join("; ", match.Ambiguous.Select(s => $"{s.Title} ({s.Year}) tvdb:{s.TvdbId}")));
            }

            inLibrary = match.Series;
        }

        var profileId = await ProfileForAsync(files, ct);
        if (inLibrary is not null)
        {
            return await AttachToSeriesAsync(inLibrary.Id, root, folderName, profileId, ct);
        }

        var (found, reason, detail) = await LookupAsync(info, ct);
        if (found is null)
        {
            return new FolderResolution(null, reason, detail);
        }

        // Known under the other id (the folder named a TMDB id, the library has the show by TVDB id, or the reverse).
        var existing = await db.Series.AsNoTracking()
            .Where(s => (found.TvdbId != null && s.TvdbId == found.TvdbId) || (found.TmdbId != null && s.TmdbId == found.TmdbId))
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is { } existingId)
        {
            return await AttachToSeriesAsync(existingId, root, folderName, profileId, ct);
        }

        var added = await seriesService.AddSeriesAsync(
            new AddSeriesRequest(
                profileId,
                root.Id,
                TvdbId: found.TvdbId,
                TmdbId: found.TvdbId is null ? found.TmdbId : null,
                Monitor: MonitoringOption.None,
                Monitored: false,
                FolderName: folderName),
            ct);
        db.ChangeTracker.Clear();

        if (!added.IsSuccess)
        {
            return new FolderResolution(null, UnmappedReason.MetadataError, $"{added.Failure!.Title}: {added.Failure.Detail}");
        }

        progress.SeriesWasAdded();
        progress.VersionAdded();
        var versionId = await db.SeriesVersions.Where(v => v.SeriesId == added.Value!.Id).Select(v => v.Id).FirstAsync(ct);
        return new FolderResolution(versionId);
    }

    /// <summary>
    /// A folder of a series already in the library: a version whose folder is not on disk adopts it (the add page
    /// named a folder that never existed), otherwise the folder becomes a new, unmonitored version.
    /// </summary>
    private async Task<FolderResolution> AttachToSeriesAsync(int seriesId, RootFolder root, string folderName, int profileId, CancellationToken ct)
    {
        var versions = await db.SeriesVersions.Include(v => v.RootFolder).Where(v => v.SeriesId == seriesId).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToListAsync(ct);
        var withoutFolder = versions.FirstOrDefault(v => !Directory.Exists(Path.Combine(v.RootFolder.Path, v.FolderName)));
        if (withoutFolder is not null && !await db.EpisodeFiles.AnyAsync(f => f.VersionId == withoutFolder.Id, ct))
        {
            withoutFolder.RootFolderId = root.Id;
            withoutFolder.FolderName = folderName;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return new FolderResolution(withoutFolder.Id);
        }

        db.ChangeTracker.Clear();
        var profile = await db.QualityProfiles.AsNoTracking().FirstAsync(p => p.Id == profileId, ct);
        var name = VersionNaming.Unique(VersionNaming.FromSeriesProfile(profile.Name), versions.Select(v => v.Name));
        var added = await seriesService.AddVersionAsync(
            seriesId,
            new AddSeriesVersionRequest(name, profileId, root.Id, folderName, MonitoringOption.None, Monitored: false),
            ct);
        db.ChangeTracker.Clear();

        return added.IsSuccess
            ? new FolderResolution(added.Value!.Id)
            : new FolderResolution(null, UnmappedReason.MetadataError, $"{added.Failure!.Title}: {added.Failure.Detail}");
    }

    private async Task<(SeriesMetadata? Found, UnmappedReason Reason, string? Detail)> LookupAsync(SeriesFolderInfo info, CancellationToken ct)
    {
        try
        {
            if (info.TvdbId is { } tvdbId)
            {
                var byTvdb = await metadata.GetByTvdbIdAsync(tvdbId, withEpisodes: false, ct);
                return byTvdb is null ? (null, UnmappedReason.NoMatch, $"tvdb:{tvdbId} unknown") : (byTvdb, default, null);
            }

            if (info.TmdbId is { } tmdbId)
            {
                var byTmdb = await metadata.GetByTmdbIdAsync(tmdbId, withEpisodes: false, ct);
                return byTmdb is null ? (null, UnmappedReason.NoMatch, $"tmdb:{tmdbId} unknown") : (byTmdb, default, null);
            }

            var candidates = await metadata.SearchAsync(info.Title, ct);
            var wanted = SeriesTitleIndex.Clean(info.Title);
            var matches = candidates.Where(c => Titles(c).Contains(wanted) && (info.Year is null || c.Year is null || Math.Abs(c.Year.Value - info.Year.Value) <= 1)).ToList();
            if (matches.Count > 1 && info.Year is not null)
            {
                var exact = matches.Where(c => c.Year == info.Year).ToList();
                if (exact.Count >= 1)
                {
                    matches = exact;
                }
            }

            // Without a year, a single exact title among several hits is the show ("Breaking Bad" vs "Breaking Bad: ...").
            if (matches.Count > 1 && info.Year is null)
            {
                var exactTitle = matches.Where(c => SeriesTitleIndex.Clean(c.Title) == wanted).ToList();
                if (exactTitle.Count == 1)
                {
                    matches = exactTitle;
                }
            }

            return matches.Count switch
            {
                1 => (matches[0], default, null),
                0 => (null, UnmappedReason.NoMatch, $"parsed '{info.Title}' ({(info.Year?.ToString() ?? "no year")}), {candidates.Count} candidate(s)"),
                _ => (null, UnmappedReason.AmbiguousMatch, string.Join("; ", matches.Take(5).Select(m => $"{m.Title} ({m.Year}) tvdb:{m.TvdbId}"))),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return (null, UnmappedReason.MetadataError, ex.Message);
        }
    }

    private static HashSet<string> Titles(SeriesMetadata candidate) =>
        new[] { candidate.Title, candidate.OriginalTitle }
            .Concat(candidate.AlternativeTitles.Select(a => a.Title))
            .Select(SeriesTitleIndex.Clean)
            .Where(t => t.Length > 0)
            .ToHashSet();

    /// <summary>The system profile by the resolution of the first file that tells one (as the film scan does).</summary>
    private async Task<int> ProfileForAsync(List<FileInfo> files, CancellationToken ct)
    {
        var profiles = await db.QualityProfiles.AsNoTracking()
            .Where(p => p.Kind == Domain.Quality.ProfileKind.Series || p.Kind == Domain.Quality.ProfileKind.Both)
            .OrderBy(p => p.Id).Select(p => new { p.Id, p.Name }).ToListAsync(ct);
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("No series quality profile is seeded; the scan has nothing to file versions under.");
        }

        var hd = profiles.FirstOrDefault(p => p.Name == QualitySeeder.SeriesHdProfileName)?.Id ?? profiles[0].Id;
        var uhd = profiles.FirstOrDefault(p => p.Name == QualitySeeder.SeriesUhdProfileName)?.Id ?? hd;
        var resolution = files.Select(f => TryParsePath(f.FullName)?.Quality?.Quality.Resolution ?? 0).FirstOrDefault(r => r > 0);
        return resolution >= QualitySeeder.UhdResolution ? uhd : hd;
    }

    private async Task<SeriesTitleIndex> BuildIndexAsync(CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking()
            .Select(s => new { s.Id, s.TvdbId, s.Title, s.OriginalTitle, s.Year, s.AlternativeTitles })
            .ToListAsync(ct);
        var tvdbIds = series.Where(s => s.TvdbId != null).Select(s => s.TvdbId!.Value).ToList();
        var aliases = await db.SceneNames.AsNoTracking().Where(n => tvdbIds.Contains(n.TvdbId)).ToListAsync(ct);

        return new SeriesTitleIndex(
            series.Select(s => new IndexedSeries(
                s.Id,
                s.TvdbId,
                s.Title,
                s.Year,
                new[] { s.OriginalTitle }.Concat(s.AlternativeTitles.Select(a => a.Title)).OfType<string>().ToList())),
            aliases.Select(a => new SceneAlias(a.TvdbId, a.Title, a.SceneSeasonNumber)));
    }

    // ---- manual mapping ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The manual answer for an unmapped folder of a series root: a series already in the library takes the folder as a
    /// version, a TVDB/TMDB id adds the show (unmonitored) on that folder. The caller rescans the series afterwards.
    /// </summary>
    public async Task<Bigarr.Api.Infrastructure.Result<(int SeriesId, int VersionId)>> MapFolderAsync(int unmappedFolderId, MapSeriesFolderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var unmapped = await db.UnmappedFolders.AsNoTracking().Include(u => u.RootFolder).FirstOrDefaultAsync(u => u.Id == unmappedFolderId, ct);
        if (unmapped is null)
        {
            return Bigarr.Api.Infrastructure.Failure.NotFound("Unmapped folder");
        }

        var root = unmapped.RootFolder;
        if (root.Kind != MediaKind.Series)
        {
            return Bigarr.Api.Infrastructure.Failure.Invalid("Not a series root folder", "Film folders are imported with the manual import.");
        }

        var folder = new DirectoryInfo(Path.Combine(root.Path, unmapped.RelativePath));
        if (!folder.Exists)
        {
            return Bigarr.Api.Infrastructure.Failure.Invalid("Folder not found", folder.FullName);
        }

        var files = EnumerateVideoFiles(folder).ToList();
        var profileId = request.QualityProfileId is { } chosen && await db.QualityProfiles.AnyAsync(p => p.Id == chosen && (p.Kind == Domain.Quality.ProfileKind.Series || p.Kind == Domain.Quality.ProfileKind.Both), ct)
            ? chosen
            : await ProfileForAsync(files, ct);

        int? seriesId = request.SeriesId;
        if (seriesId is null && (request.TvdbId is not null || request.TmdbId is not null))
        {
            seriesId = await db.Series.AsNoTracking()
                .Where(s => (request.TvdbId != null && s.TvdbId == request.TvdbId) || (request.TmdbId != null && s.TmdbId == request.TmdbId))
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(ct);
        }

        FolderResolution resolution;
        if (seriesId is { } existing)
        {
            if (!await db.Series.AnyAsync(s => s.Id == existing, ct))
            {
                return Bigarr.Api.Infrastructure.Failure.NotFound("Series");
            }

            resolution = await AttachToSeriesAsync(existing, root, unmapped.RelativePath, profileId, ct);
        }
        else if (request.TvdbId is not null || request.TmdbId is not null)
        {
            var added = await seriesService.AddSeriesAsync(
                new AddSeriesRequest(profileId, root.Id, request.TvdbId, request.TvdbId is null ? request.TmdbId : null, Monitor: MonitoringOption.None, Monitored: false, FolderName: unmapped.RelativePath),
                ct);
            db.ChangeTracker.Clear();
            if (!added.IsSuccess)
            {
                return added.Failure!;
            }

            resolution = new FolderResolution(await db.SeriesVersions.Where(v => v.SeriesId == added.Value!.Id).Select(v => v.Id).FirstAsync(ct));
        }
        else
        {
            return Bigarr.Api.Infrastructure.Failure.Invalid("A series is required", "Give a library series id, or a TVDB or TMDB id to add.");
        }

        if (resolution.VersionId is not { } versionId)
        {
            return Bigarr.Api.Infrastructure.Failure.Invalid("Could not map the folder", resolution.Detail);
        }

        await db.UnmappedFolders.Where(u => u.Id == unmappedFolderId).ExecuteDeleteAsync(ct);
        var owner = await db.SeriesVersions.Where(v => v.Id == versionId).Select(v => v.SeriesId).FirstAsync(ct);
        return Bigarr.Api.Infrastructure.Result<(int, int)>.Ok((owner, versionId));
    }

    // ---- one series ---------------------------------------------------------------------------------------------------

    /// <summary>Rescans every version folder of a series. A folder missing on disk is skipped, its files left as they are.</summary>
    public async Task ScanSeriesAsync(int seriesId, ScanProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var versions = await db.SeriesVersions.AsNoTracking().Where(v => v.SeriesId == seriesId).OrderBy(v => v.Id).Select(v => v.Id).ToListAsync(ct);
        if (versions.Count == 0)
        {
            throw new InvalidOperationException($"Series {seriesId} no longer exists.");
        }

        progress.FoldersTotal = versions.Count;
        foreach (var versionId in versions)
        {
            await ScanVersionAsync(versionId, progress, ct);
            progress.FolderDone();
        }
    }

    /// <summary>Attaches the files of one version folder to their episodes; files that do not fit are listed as unmatched.</summary>
    public async Task ScanVersionAsync(int versionId, ScanProgress progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var version = await db.SeriesVersions.Include(v => v.RootFolder).Include(v => v.Series).FirstAsync(v => v.Id == versionId, ct);
        var folderPath = Path.Combine(version.RootFolder.Path, version.FolderName);
        progress.CurrentFolder = version.FolderName;
        if (!Directory.Exists(folderPath))
        {
            logger.LogInformation("{Series}: folder {Folder} of version {Version} is not on disk, skipped", version.Series.Title, folderPath, version.Name);
            db.ChangeTracker.Clear();
            return;
        }

        var series = version.Series;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var episodes = await db.Episodes.Where(e => e.SeriesId == series.Id).ToListAsync(ct);
        var targets = await db.EpisodeTargets.Where(t => t.VersionId == versionId).Include(t => t.EpisodeFile).ToListAsync(ct);
        var targetsByEpisode = targets.GroupBy(t => t.EpisodeId).ToDictionary(g => g.Key, g => g.First());
        var files = await db.EpisodeFiles.Where(f => f.VersionId == versionId).ToListAsync(ct);
        var known = files.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        var unmatched = await db.UnmatchedEpisodeFiles.Where(u => u.VersionId == versionId).ToListAsync(ct);
        var unmatchedByPath = unmatched.ToDictionary(u => u.RelativePath, StringComparer.Ordinal);
        var aliases = series.TvdbId is { } tvdbId
            ? await db.SceneNames.AsNoTracking().Where(n => n.TvdbId == tvdbId && n.SceneSeasonNumber != null).ToListAsync(ct)
            : [];
        var sceneSeasonByTitle = aliases.GroupBy(a => a.CleanTitle).ToDictionary(g => g.Key, g => g.First().SceneSeasonNumber!.Value);
        var mappable = MappableSeries.From(series);
        var definitions = await db.QualityDefinitions.AsNoTracking().Select(q => q.Id).ToListAsync(ct);

        foreach (var file in EnumerateVideoFiles(new DirectoryInfo(folderPath)))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(folderPath, file.FullName);
            seen.Add(relativePath);

            if (known.TryGetValue(relativePath, out var existing))
            {
                existing.Size = file.Length;
                continue;
            }

            var parsed = TryParsePath(file.FullName);
            EpisodeMapping mapping;
            if (parsed is { FullSeason: true })
            {
                mapping = EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, $"Named like a season pack ({parsed}), not one episode.");
            }
            else
            {
                // Sonarr reads files already in the library with TVDB numbers (sceneSource: false). A file named after an
                // XEM scene-season name ("Show S2 - 05") is scene-numbered by its very name, so XEM applies to it.
                int? sceneSeason = parsed?.SeriesTitle is { Length: > 0 } title && sceneSeasonByTitle.TryGetValue(SeriesTitleIndex.Clean(title), out var s) ? s : null;
                mapping = EpisodeMapper.Map(parsed, mappable, episodes, sceneSource: sceneSeason is not null, sceneSeason);

                // A series shown in another order has its library named after that order (blueprint §8): a file of
                // its folder is read on the order's numbers first, TVDB's when the order has no such episode.
                if (sceneSeason is null && mappable.Ordering != EpisodeOrder.Tvdb && EpisodeMapper.MapOrder(parsed, mappable, episodes) is { IsMapped: true } byOrder)
                {
                    mapping = byOrder;
                }
            }

            if (!mapping.IsMapped)
            {
                Unmatched(relativePath, file.Length, ReasonOf(mapping.Status, parsed), mapping.Detail);
                continue;
            }

            var holders = mapping.Episodes.Select(e => targetsByEpisode.GetValueOrDefault(e.Id)).ToList();
            var taken = mapping.Episodes.Zip(holders).Where(p => p.Second?.EpisodeFile is not null).ToList();
            if (taken.Count > 0)
            {
                Unmatched(relativePath, file.Length, UnmatchedFileReason.Duplicate,
                    string.Join("; ", taken.Select(p => $"S{p.First.SeasonNumber:00}E{p.First.EpisodeNumber:00} already has {p.Second!.EpisodeFile!.RelativePath}")));
                continue;
            }

            var quality = parsed!.Quality ?? new QualityModel(Parsing.Qualities.Quality.Unknown);
            var episodeFile = new EpisodeFile
            {
                VersionId = versionId,
                RelativePath = relativePath,
                SeasonNumber = mapping.Episodes[0].SeasonNumber,
                Size = file.Length,
                DateAdded = Now,
                QualityId = definitions.Contains(quality.Quality.Id) ? quality.Quality.Id : Parsing.Qualities.Quality.Unknown.Id,
                Revision = new Domain.Movies.Revision
                {
                    Version = quality.Revision?.Version ?? 1,
                    Real = quality.Revision?.Real ?? 0,
                    IsRepack = quality.Revision?.IsRepack ?? false,
                },
                ReleaseGroup = string.IsNullOrWhiteSpace(parsed.ReleaseGroup) ? null : parsed.ReleaseGroup,
                Languages = parsed.Languages?.Select(l => l.Name).Where(n => n is not null).Cast<string>().ToList() ?? [],
                OriginalReleaseTitle = Path.GetFileNameWithoutExtension(file.Name),
            };
            db.EpisodeFiles.Add(episodeFile);

            for (var i = 0; i < mapping.Episodes.Count; i++)
            {
                var target = holders[i];
                if (target is null)
                {
                    // An episode the version has no row for yet (added by a refresh before this version existed).
                    target = new EpisodeTarget { VersionId = versionId, EpisodeId = mapping.Episodes[i].Id, Monitored = false, CreatedAt = Now };
                    db.EpisodeTargets.Add(target);
                    targetsByEpisode[mapping.Episodes[i].Id] = target;
                }

                target.EpisodeFile = episodeFile;
            }

            if (unmatchedByPath.Remove(relativePath, out var stale))
            {
                db.UnmatchedEpisodeFiles.Remove(stale);
            }

            progress.FileAdded();
        }

        // Unmatched files that are gone from disk are forgotten (the files themselves were never touched).
        db.UnmatchedEpisodeFiles.RemoveRange(unmatchedByPath.Values.Where(u => u.Id != 0 && !seen.Contains(u.RelativePath)));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        void Unmatched(string relativePath, long size, UnmatchedFileReason reason, string? detail)
        {
            detail = detail is { Length: > 2000 } ? detail[..2000] : detail;
            if (unmatchedByPath.TryGetValue(relativePath, out var row))
            {
                row.Size = size;
                row.Reason = reason;
                row.Detail = detail;
                row.LastSeenAt = Now;
            }
            else
            {
                row = new UnmatchedEpisodeFile
                {
                    SeriesId = series.Id,
                    VersionId = versionId,
                    RelativePath = relativePath,
                    Size = size,
                    Reason = reason,
                    Detail = detail,
                    DiscoveredAt = Now,
                    LastSeenAt = Now,
                };
                db.UnmatchedEpisodeFiles.Add(row);
                unmatchedByPath[relativePath] = row;
            }

            progress.FileUnmatched();
        }
    }

    private static UnmatchedFileReason ReasonOf(EpisodeMappingStatus status, ParsedEpisodeInfo? parsed) => status switch
    {
        EpisodeMappingStatus.NotParsable when parsed is { FullSeason: true } => UnmatchedFileReason.NotAnEpisode,
        EpisodeMappingStatus.NotParsable => UnmatchedFileReason.NotParsable,
        EpisodeMappingStatus.Ambiguous => UnmatchedFileReason.Ambiguous,
        EpisodeMappingStatus.WrongSeriesType => UnmatchedFileReason.WrongSeriesType,
        _ => UnmatchedFileReason.EpisodeNotFound,
    };

    // ---- files ------------------------------------------------------------------------------------------------------

    public static IEnumerable<FileInfo> EnumerateVideoFiles(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        return folder.EnumerateFiles("*", options)
            .Where(f => VideoFiles.IsVideo(f.Name) && !VideoFiles.LooksLikeSample(f.Name) && !IsUnderExtras(folder, f))
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsUnderExtras(DirectoryInfo root, FileInfo file)
    {
        for (var dir = file.Directory; dir is not null && dir.FullName != root.FullName; dir = dir.Parent)
        {
            if (ExtrasFolders.Contains(dir.Name) || dir.Name.StartsWith('.'))
            {
                return true;
            }
        }

        return false;
    }

    private static ParsedEpisodeInfo? TryParsePath(string path)
    {
        try
        {
            return EpisodeParser.ParsePath(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task UpsertUnmappedAsync(int rootFolderId, string relativePath, int fileCount, UnmappedReason reason, string? detail, CancellationToken ct)
    {
        detail = detail is { Length: > 2000 } ? detail[..2000] : detail;
        var existing = await db.UnmappedFolders.FirstOrDefaultAsync(u => u.RootFolderId == rootFolderId && u.RelativePath == relativePath, ct);
        if (existing is null)
        {
            db.UnmappedFolders.Add(new UnmappedFolder
            {
                RootFolderId = rootFolderId,
                RelativePath = relativePath,
                FileCount = fileCount,
                Reason = reason,
                Detail = detail,
                DiscoveredAt = Now,
                LastSeenAt = Now,
            });
        }
        else
        {
            existing.FileCount = fileCount;
            existing.Reason = reason;
            existing.Detail = detail;
            existing.LastSeenAt = Now;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
