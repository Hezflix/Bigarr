using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Library;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Library;

public static class RootFoldersEndpoints
{
    public static IEndpointRouteBuilder MapRootFolders(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/rootfolders").WithTags("Library").RequireAuthorization(AuthPolicies.Api);

        group.MapGet("/", ListAsync);
        group.MapPost("/", AddAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapGet("/unmapped", ListUnmappedAsync);
        group.MapGet("/{id:int}/unmapped", ListUnmappedForRootAsync);
        group.MapPost("/{id:int}/scan", StartScanAsync);
        group.MapGet("/{id:int}/scan", ScanStatusAsync);

        return app;
    }

    private static async Task<Ok<List<RootFolderResponse>>> ListAsync(BigarrDbContext db, CancellationToken ct)
    {
        var roots = await db.RootFolders.AsNoTracking().OrderBy(r => r.Path).ToListAsync(ct);
        var versionCounts = await db.MovieVersions.GroupBy(v => v.RootFolderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var unmappedCounts = await db.UnmappedFolders.GroupBy(u => u.RootFolderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return TypedResults.Ok(roots.Select(r => ToResponse(r, versionCounts.GetValueOrDefault(r.Id), unmappedCounts.GetValueOrDefault(r.Id))).ToList());
    }

    private static async Task<Results<Created<RootFolderResponse>, ProblemHttpResult>> AddAsync(AddRootFolderRequest request, BigarrDbContext db, CancellationToken ct)
    {
        var path = NormalizePath(request.Path);
        if (path is null)
        {
            return Failure.Invalid("Invalid path", "The path must be absolute.").ToProblem();
        }

        if (!Directory.Exists(path))
        {
            return Failure.Invalid("Folder not found", $"'{path}' does not exist or is not accessible from Bigarr.").ToProblem();
        }

        if (await db.RootFolders.AnyAsync(r => r.Path == path, ct))
        {
            return Failure.Conflict("Root folder already exists", path).ToProblem();
        }

        var root = new RootFolder { Path = path };
        db.RootFolders.Add(root);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/v1/rootfolders/{root.Id}", ToResponse(root, 0, 0));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(int id, BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct)
    {
        var root = await db.RootFolders.FindAsync([id], ct);
        if (root is null)
        {
            return Failure.NotFound("Root folder").ToProblem();
        }

        var inUse = await db.MovieVersions.CountAsync(v => v.RootFolderId == id, ct)
                    + await db.Movies.CountAsync(m => m.DefaultRootFolderId == id, ct);
        if (inUse > 0)
        {
            return Failure.Conflict("Root folder in use", $"{inUse} version(s) or movie default(s) still point at '{root.Path}'. Move them first.").ToProblem();
        }

        db.RootFolders.Remove(root);
        await db.SaveChangesAsync(ct);
        scans.Forget(id);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<UnmappedFolderResponse>>> ListUnmappedAsync(BigarrDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await UnmappedAsync(db, null, ct));

    private static async Task<Results<Ok<List<UnmappedFolderResponse>>, NotFound>> ListUnmappedForRootAsync(int id, BigarrDbContext db, CancellationToken ct)
    {
        if (!await db.RootFolders.AnyAsync(r => r.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await UnmappedAsync(db, id, ct));
    }

    private static async Task<Results<Accepted<ScanStatusResponse>, NotFound>> StartScanAsync(int id, BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct)
    {
        if (!await db.RootFolders.AnyAsync(r => r.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Accepted($"/api/v1/rootfolders/{id}/scan", scans.Enqueue(id));
    }

    private static async Task<Results<Ok<ScanStatusResponse>, NotFound>> ScanStatusAsync(int id, BigarrDbContext db, LibraryScanRunner scans, CancellationToken ct)
    {
        if (!await db.RootFolders.AnyAsync(r => r.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(scans.Status(id));
    }

    private static async Task<List<UnmappedFolderResponse>> UnmappedAsync(BigarrDbContext db, int? rootFolderId, CancellationToken ct)
    {
        var query = db.UnmappedFolders.AsNoTracking().Include(u => u.RootFolder).AsQueryable();
        if (rootFolderId is not null)
        {
            query = query.Where(u => u.RootFolderId == rootFolderId);
        }

        var rows = await query.OrderBy(u => u.RootFolder.Path).ThenBy(u => u.RelativePath).ToListAsync(ct);
        return rows.Select(u => new UnmappedFolderResponse(
            u.Id, u.RootFolderId, u.RelativePath, Path.Combine(u.RootFolder.Path, u.RelativePath), u.FileCount, u.Reason, u.Detail, u.DiscoveredAt, u.LastSeenAt)).ToList();
    }

    private static RootFolderResponse ToResponse(RootFolder root, int versionCount, int unmappedCount)
    {
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

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path.Trim());
        return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
}
