using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

/// <summary>Root folders: the list with their disk state, adding one, and the path normalization every caller must share.</summary>
public sealed class RootFolderService(BigarrDbContext db)
{
    public async Task<List<RootFolderResponse>> ListAsync(CancellationToken ct)
    {
        var roots = await db.RootFolders.AsNoTracking().OrderBy(r => r.Path).ToListAsync(ct);
        var versionCounts = await db.MovieVersions.GroupBy(v => v.RootFolderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var unmappedCounts = await db.UnmappedFolders.GroupBy(u => u.RootFolderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return roots.Select(r => ToResponse(r, versionCounts.GetValueOrDefault(r.Id), unmappedCounts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<RootFolderResponse?> GetAsync(int id, CancellationToken ct)
    {
        var root = await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (root is null)
        {
            return null;
        }

        var versions = await db.MovieVersions.CountAsync(v => v.RootFolderId == id, ct);
        var unmapped = await db.UnmappedFolders.CountAsync(u => u.RootFolderId == id, ct);
        return ToResponse(root, versions, unmapped);
    }

    /// <summary>The root folder whose path matches <paramref name="path"/> once normalized (trailing separators dropped), or null.</summary>
    public async Task<RootFolderResponse?> FindByPathAsync(string? path, CancellationToken ct)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
        {
            return null;
        }

        var root = await db.RootFolders.AsNoTracking().FirstOrDefaultAsync(r => r.Path == normalized, ct);
        return root is null ? null : await GetAsync(root.Id, ct);
    }

    public async Task<Result<RootFolderResponse>> AddAsync(string? requestedPath, CancellationToken ct)
    {
        var path = NormalizePath(requestedPath);
        if (path is null)
        {
            return Failure.Invalid("Invalid path", "The path must be absolute.");
        }

        if (!Directory.Exists(path))
        {
            return Failure.Invalid("Folder not found", $"'{path}' does not exist or is not accessible from Bigarr.");
        }

        if (await db.RootFolders.AnyAsync(r => r.Path == path, ct))
        {
            return Failure.Conflict("Root folder already exists", path);
        }

        var root = new RootFolder { Path = path };
        db.RootFolders.Add(root);
        await db.SaveChangesAsync(ct);
        return Result<RootFolderResponse>.Ok(ToResponse(root, 0, 0));
    }

    public static RootFolderResponse ToResponse(RootFolder root, int versionCount, int unmappedCount)
    {
        ArgumentNullException.ThrowIfNull(root);

        var accessible = Directory.Exists(root.Path);
        long? free = null, total = null;
        if (accessible)
        {
            try
            {
                var drive = new DriveInfo(root.Path);
                free = drive.AvailableFreeSpace;
                total = drive.TotalSize;
            }
            catch (Exception)
            {
                // free space is a nicety
            }
        }

        return new RootFolderResponse(root.Id, root.Path, accessible, free, total, versionCount, unmappedCount);
    }

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path.Trim());
        return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
}
