using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Auth;
using Bigarr.Api.Infrastructure.Persistence;
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

    private static async Task<Ok<List<RootFolderResponse>>> ListAsync(RootFolderService roots, CancellationToken ct) =>
        TypedResults.Ok(await roots.ListAsync(ct));

    private static async Task<Results<Created<RootFolderResponse>, ProblemHttpResult>> AddAsync(AddRootFolderRequest request, RootFolderService roots, CancellationToken ct)
    {
        var result = await roots.AddAsync(request.Path, ct);
        return result.IsSuccess ? TypedResults.Created($"/api/v1/rootfolders/{result.Value!.Id}", result.Value) : result.Failure!.ToProblem();
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
}
