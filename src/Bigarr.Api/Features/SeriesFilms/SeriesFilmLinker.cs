using Bigarr.Api.Features.Import;
using Bigarr.Api.Infrastructure.Files;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.SeriesFilms;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>What one reconciliation did, for logs and tests.</summary>
public sealed record LinkPass(int Created, int Removed, int KeptAsFiles, int Failed);

/// <summary>
/// Keeps the specials of linked films in step with the films' files (S8). For each link, in each version of the series
/// whose target for the special is monitored, the special is a hardlink of the film's default-version file, named by
/// the series naming (<c>Specials/Show - S00E02 - Title [Quality].mkv</c>) and recorded as an <see cref="EpisodeFile"/>
/// flagged as linked. The film engine imports, upgrades, renames and deletes; this keeper follows:
/// <list type="bullet">
/// <item>film file imported or upgraded (a new <c>MediaFile</c>): the stale name goes, a new hardlink is made;</item>
/// <item>film file renamed: same inode, nothing to redo, the recorded source path follows;</item>
/// <item>film file deleted (to the recycle bin or for good): the special's name goes too, the bytes follow the film's
/// bin rules through the film's own name;</item>
/// <item>link removed (or film deleted keeping its files): a special whose film file still exists stays as a plain
/// episode file (still one copy on disk); one whose film file is gone is removed.</item>
/// </list>
/// Never a copy: when the hardlink fails (two filesystems, a bind mount), the link records why and health reports it.
/// A plain file the special has of its own is <b>adopted</b> when the film has none: it is moved into the film's folder
/// (a rename on the same filesystem, never a copy) and imported there by the film engine, then hardlinked back. When
/// the film already has its file, the film's wins and the special's own file goes to the recycle bin.
/// <see cref="SeriesFilmPlanner"/> runs these rules on paper for the review page.
/// </summary>
public sealed class SeriesFilmLinker(BigarrDbContext db, RecycleBin recycleBin, ImportService importer, TimeProvider time, ILogger<SeriesFilmLinker> logger)
{
    /// <summary>The adopted file's name in the film's folder until the film engine imports it under the film naming.</summary>
    private const string AdoptPrefix = ".bigarr-adopt-";

    public Task<LinkPass> ReconcileMovieAsync(int movieId, CancellationToken ct) => ReconcileAsync(l => l.MovieId == movieId, ct);

    public Task<LinkPass> ReconcileSeriesAsync(int seriesId, CancellationToken ct) => ReconcileAsync(l => l.SeriesId == seriesId, ct);

    public Task<LinkPass> ReconcileAllAsync(CancellationToken ct) => ReconcileAsync(l => true, ct);

    /// <summary>Links matching <paramref name="links"/>, plus every orphaned hardlink (rare, and their link row is gone).</summary>
    private async Task<LinkPass> ReconcileAsync(System.Linq.Expressions.Expression<Func<SeriesMovieLink, bool>> links, CancellationToken ct)
    {
        var config = await db.MediaManagement.AsNoTracking().FirstOrDefaultAsync(c => c.Id == MediaManagementConfig.SingletonId, ct) ?? MediaManagementConfig.Default();
        var pass = new Counter();

        // Hardlinks whose link is gone (the user unlinked, the film was deleted): a plain file if the film file stays, else removed.
        var orphans = await db.EpisodeFiles
            .Include(f => f.Version).ThenInclude(v => v.RootFolder)
            .Where(f => f.LinkedFromPath != null && f.SeriesMovieLinkId == null)
            .ToListAsync(ct);
        foreach (var orphan in orphans)
        {
            await SettleOrphanAsync(orphan, pass, ct);
        }

        var rows = await db.SeriesMovieLinks
            .Include(l => l.Series).ThenInclude(s => s.Versions).ThenInclude(v => v.RootFolder)
            .Include(l => l.Episode)
            .Include(l => l.Movie).ThenInclude(m => m.Versions).ThenInclude(v => v.File!).ThenInclude(f => f.Quality)
            .Include(l => l.Movie).ThenInclude(m => m.Versions).ThenInclude(v => v.RootFolder)
            .Where(links)
            .AsSplitQuery()
            .ToListAsync(ct);

        foreach (var link in rows)
        {
            await ReconcileLinkAsync(link, config, pass, ct);
        }

        await db.SaveChangesAsync(ct);
        var result = new LinkPass(pass.Created, pass.Removed, pass.Kept, pass.Failed);
        if (result != new LinkPass(0, 0, 0, 0))
        {
            logger.LogInformation("Series film links: {Created} created, {Removed} removed, {Kept} kept as plain files, {Failed} failed", result.Created, result.Removed, result.KeptAsFiles, result.Failed);
        }

        return result;
    }

    /// <summary>The film's file the specials point at: the default version's, on disk.</summary>
    public static (Bigarr.Domain.Movies.MediaFile File, string Path)? SourceOf(Bigarr.Domain.Movies.Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var version = movie.Versions.FirstOrDefault(v => v.IsDefault);
        if (version?.File is not { } file)
        {
            return null;
        }

        var path = Path.Combine(version.RootFolder.Path, version.FolderName, file.RelativePath);
        return File.Exists(path) ? (file, path) : null;
    }

    /// <summary>What the series naming needs to know of the film's file.</summary>
    public static EpisodeFileFacts FactsOf(Bigarr.Domain.Movies.MediaFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new EpisodeFileFacts(file.Quality?.Name, file.Revision.Version, file.Revision.Real, file.Revision.IsRepack, file.ReleaseGroup, file.Languages, file.MediaInfo, [], file.OriginalReleaseTitle, Path.GetFileNameWithoutExtension(path));
    }

    private async Task ReconcileLinkAsync(SeriesMovieLink link, MediaManagementConfig config, Counter pass, CancellationToken ct)
    {
        var source = SourceOf(link.Movie);
        var versionIds = link.Series.Versions.Select(v => v.Id).ToList();
        var targets = await db.EpisodeTargets
            .Include(t => t.EpisodeFile!).ThenInclude(f => f.Episodes)
            .Where(t => t.EpisodeId == link.EpisodeId && versionIds.Contains(t.VersionId))
            .ToListAsync(ct);

        string? failure = null;
        if (source is null && link.Movie.Versions.FirstOrDefault(v => v.IsDefault) is { } filmVersion
            && SeriesFilmPlanner.Adoptable(link.Series.Versions, targets, link.EpisodeId, (t, _) => t.Monitored) is { } adoption)
        {
            failure = await AdoptAsync(link, filmVersion, adoption, pass, ct);
            source = SourceOf(link.Movie);
            if (failure is not null)
            {
                link.LinkError = failure;
                link.LinkErrorAt = time.GetUtcNow().UtcDateTime;
                return;
            }
        }

        foreach (var version in link.Series.Versions)
        {
            var target = targets.FirstOrDefault(t => t.VersionId == version.Id);
            if (target is null)
            {
                continue;
            }

            var folder = Path.Combine(version.RootFolder.Path, version.FolderName);
            var current = target.EpisodeFile;
            if (source is not { } src || !target.Monitored)
            {
                // No film file (yet, or any more), or this version does not want the special: no hardlink here.
                if (current is { IsFilmLink: true })
                {
                    RemoveLinkedFile(current, target, folder, pass);
                }

                continue;
            }

            var extension = Path.GetExtension(src.Path);
            var facts = FactsOf(src.File, src.Path);
            var relative = SeriesNaming.RelativePath(config, link.Series, version, [link.Episode], facts, extension, Path.GetFileName(src.Path));
            var destination = Path.Combine(folder, relative);

            if (current is { IsFilmLink: true } && current.LinkedMediaFileId == src.File.Id && current.RelativePath == relative && File.Exists(destination))
            {
                // Same film file (a rename keeps the inode): only the recorded source moves.
                current.LinkedFromPath = src.Path;
                current.SeriesMovieLinkId = link.Id;
                continue;
            }

            if (current is { IsFilmLink: false })
            {
                if (current.Episodes.Any(t => t.EpisodeId != link.EpisodeId))
                {
                    failure = $"{version.Name}: the special shares a file with other episodes ({current.RelativePath}); left as is";
                    pass.Failed++;
                    continue;
                }

                // The special had a file of its own: the film's replaces it, through the recycle bin.
                recycleBin.RemoveFile(Path.Combine(folder, current.RelativePath), config.RecycleBinPath, version.RootFolder.Path);
                db.EpisodeFiles.Remove(current);
                target.EpisodeFile = null;
                target.EpisodeFileId = null;
                pass.Removed++;
            }
            else if (current is { IsFilmLink: true })
            {
                RemoveLinkedFile(current, target, folder, pass);
            }

            if (current is not null)
            {
                // The new hardlink's row may take the old one's name (a re-import keeping its name): the removal goes first.
                await db.SaveChangesAsync(ct);
            }

            if (File.Exists(destination))
            {
                // Something untracked sits where the special goes: it goes to the bin rather than being overwritten.
                recycleBin.RemoveFile(destination, config.RecycleBinPath, version.RootFolder.Path);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!FileTransfer.TryHardlink(src.Path, destination, out var reason))
            {
                failure = $"{version.Name}: cannot hardlink {src.Path} to {destination} ({reason}); not copied";
                logger.LogWarning("Series film link {LinkId}: {Failure}", link.Id, failure);
                pass.Failed++;
                continue;
            }

            var file = new EpisodeFile
            {
                VersionId = version.Id,
                RelativePath = relative,
                SeasonNumber = link.Episode.SeasonNumber,
                Size = src.File.Size,
                DateAdded = time.GetUtcNow().UtcDateTime,
                QualityId = src.File.QualityId,
                Revision = new Bigarr.Domain.Movies.Revision { Version = src.File.Revision.Version, Real = src.File.Revision.Real, IsRepack = src.File.Revision.IsRepack },
                ReleaseGroup = src.File.ReleaseGroup,
                Languages = [.. src.File.Languages],
                IndexerFlags = src.File.IndexerFlags,
                OriginalReleaseTitle = src.File.OriginalReleaseTitle,
                MediaInfo = src.File.MediaInfo,
                SeriesMovieLinkId = link.Id,
                LinkedFromPath = src.Path,
                LinkedMediaFileId = src.File.Id,
            };
            db.EpisodeFiles.Add(file);
            target.EpisodeFile = file;
            pass.Created++;
        }

        link.LinkError = failure;
        link.LinkErrorAt = failure is null ? null : time.GetUtcNow().UtcDateTime;
    }

    /// <summary>
    /// The film has no file and the special has one of its own: it becomes the film's file. A hardlink under a hidden
    /// name in the film's folder (refused across filesystems: never a copy), imported there by the film engine with a
    /// move (a rename in the same folder) under the film naming, with its quality and media info; then the special's
    /// own name goes and the keeper hardlinks the film's file back. Null when done, else why (the special is untouched).
    /// </summary>
    private async Task<string?> AdoptAsync(SeriesMovieLink link, Bigarr.Domain.Movies.MovieVersion filmVersion, SeriesFilmPlanner.Adoption adoption, Counter pass, CancellationToken ct)
    {
        var filmFolder = Path.Combine(filmVersion.RootFolder.Path, filmVersion.FolderName);
        var createdFolder = !Directory.Exists(filmFolder);
        var staging = Path.Combine(filmFolder, AdoptPrefix + Guid.NewGuid().ToString("N") + Path.GetExtension(adoption.Path));
        string? reason;
        try
        {
            Directory.CreateDirectory(filmFolder);
            if (!FileTransfer.TryHardlink(adoption.Path, staging, out reason))
            {
                reason = $"{adoption.Version.Name}: cannot move {adoption.Path} into {filmFolder} ({reason}); not copied, the special keeps its file";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"{adoption.Version.Name}: cannot move {adoption.Path} into {filmFolder} ({ex.Message}); not copied, the special keeps its file";
        }

        if (reason is not null)
        {
            CleanUp(null, createdFolder ? filmFolder : null);
            logger.LogWarning("Series film link {LinkId}: {Failure}", link.Id, reason);
            pass.Failed++;
            return reason;
        }

        var file = adoption.File;
        var version = await importer.LoadVersionAsync(filmVersion.Id, ct);
        if (version is null)
        {
            CleanUp(staging, createdFolder ? filmFolder : null);
            return "the film's default version is gone";
        }

        var quality = new Bigarr.Parsing.Qualities.QualityModel(
            Bigarr.Parsing.Qualities.Quality.All.FirstOrDefault(q => q.Id == file.QualityId) ?? Bigarr.Parsing.Qualities.Quality.Unknown,
            new Bigarr.Parsing.Qualities.Revision(file.Revision.Version, file.Revision.Real, file.Revision.IsRepack));
        var parsed = new Bigarr.Parsing.Model.ParsedMovieInfo
        {
            Quality = quality,
            ReleaseGroup = file.ReleaseGroup!,
            Languages = [.. Bigarr.Parsing.Languages.Language.All.Where(l => l.Id > 0 && file.Languages.Contains(l.Name, StringComparer.OrdinalIgnoreCase))],
        };
        var releaseTitle = file.OriginalReleaseTitle ?? Path.GetFileNameWithoutExtension(adoption.Path);
        ImportOutcome outcome;
        try
        {
            outcome = await importer.ExecuteAsync(
                new ImportPlan(staging, version, releaseTitle, parsed, quality, file.IndexerFlags, null, Force: false, TransferMode.Move, ExtraPaths: [], AdoptedFrom: adoption.Path),
                ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome = ImportOutcome.Failed(ex.Message);
        }

        if (!outcome.Success)
        {
            CleanUp(staging, createdFolder ? filmFolder : null);
            var failure = $"{adoption.Version.Name}: the film could not take {adoption.Path} ({outcome.Reason}); the special keeps its file";
            logger.LogWarning("Series film link {LinkId}: {Failure}", link.Id, failure);
            pass.Failed++;
            return failure;
        }

        // The film's name holds the bytes now: the special's own name goes, the hardlink pass puts the film's file back.
        try
        {
            File.Delete(adoption.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the adopted special's name {Path}", adoption.Path);
        }

        adoption.Target.EpisodeFile = null;
        adoption.Target.EpisodeFileId = null;
        db.EpisodeFiles.Remove(file);

        // Saved before the hardlink's row is added: it often takes the very same name (one row per version and path).
        await db.SaveChangesAsync(ct);
        if (link.Movie.Versions.FirstOrDefault(v => v.Id == filmVersion.Id)?.File is { } adopted)
        {
            await db.Entry(adopted).Reference(f => f.Quality).LoadAsync(ct);
        }

        logger.LogInformation("Series film link {LinkId}: the film {Title} adopted the special's file {Path}", link.Id, link.Movie.Title, adoption.Path);
        return null;
    }

    private void CleanUp(string? staging, string? emptyFolder)
    {
        try
        {
            if (staging is not null && File.Exists(staging))
            {
                File.Delete(staging);
            }

            if (emptyFolder is not null && Directory.Exists(emptyFolder) && !Directory.EnumerateFileSystemEntries(emptyFolder).Any())
            {
                Directory.Delete(emptyFolder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clean up after a refused adoption in {Folder}", emptyFolder ?? staging);
        }
    }

    private void RemoveLinkedFile(EpisodeFile file, EpisodeTarget? target, string folder, Counter pass)
    {
        // A hardlink of the film's file: removing this name frees nothing, the film's name keeps the bytes (or its bin copy).
        var path = Path.Combine(folder, file.RelativePath);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove the film hardlink {Path}", path);
        }

        if (target is not null)
        {
            target.EpisodeFile = null;
            target.EpisodeFileId = null;
        }

        db.EpisodeFiles.Remove(file);
        pass.Removed++;
    }

    private async Task SettleOrphanAsync(EpisodeFile orphan, Counter pass, CancellationToken ct)
    {
        var folder = Path.Combine(orphan.Version.RootFolder.Path, orphan.Version.FolderName);
        if (orphan.LinkedFromPath is { } from && File.Exists(from) && File.Exists(Path.Combine(folder, orphan.RelativePath)))
        {
            // The film file is still there: the special keeps it as an ordinary episode file (no extra space).
            orphan.LinkedFromPath = null;
            orphan.LinkedMediaFileId = null;
            pass.Kept++;
            return;
        }

        var targets = await db.EpisodeTargets.Where(t => t.EpisodeFileId == orphan.Id).ToListAsync(ct);
        RemoveLinkedFile(orphan, null, folder, pass);
        foreach (var target in targets)
        {
            target.EpisodeFileId = null;
        }
    }

    private sealed class Counter
    {
        public int Created { get; set; }

        public int Removed { get; set; }

        public int Kept { get; set; }

        public int Failed { get; set; }
    }
}
