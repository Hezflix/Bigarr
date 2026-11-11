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
/// A plain file the special had before it was linked goes to the recycle bin once the film's file replaces it.
/// </summary>
public sealed class SeriesFilmLinker(BigarrDbContext db, RecycleBin recycleBin, TimeProvider time, ILogger<SeriesFilmLinker> logger)
{
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

    private async Task ReconcileLinkAsync(SeriesMovieLink link, MediaManagementConfig config, Counter pass, CancellationToken ct)
    {
        var source = SourceOf(link.Movie);
        var versionIds = link.Series.Versions.Select(v => v.Id).ToList();
        var targets = await db.EpisodeTargets
            .Include(t => t.EpisodeFile!).ThenInclude(f => f.Episodes)
            .Where(t => t.EpisodeId == link.EpisodeId && versionIds.Contains(t.VersionId))
            .ToListAsync(ct);

        string? failure = null;
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
            var facts = new EpisodeFileFacts(src.File.Quality?.Name, src.File.Revision.Version, src.File.Revision.Real, src.File.Revision.IsRepack, src.File.ReleaseGroup, src.File.Languages, src.File.MediaInfo, [], src.File.OriginalReleaseTitle, Path.GetFileNameWithoutExtension(src.Path));
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
