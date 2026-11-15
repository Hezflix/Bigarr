using Bigarr.Api.Features.Library;
using Bigarr.Domain.Library;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary><c>/rootfolder</c>: Seerr offers them at request time, Bazarr checks their reachability before every sync.</summary>
public static class RootFoldersEndpoints
{
    /// <param name="kind">Films for the Radarr surface, series for the Sonarr one.</param>
    public static void MapCompatRootFolders(this RouteGroupBuilder group, MediaKind kind = MediaKind.Movies)
    {
        group.MapGet("/rootfolder", async Task<JsonHttpResult<List<RadarrRootFolder>>> (RootFolderService roots, CancellationToken ct) =>
            RadarrCompat.Ok((await roots.ListAsync(ct)).Where(r => r.Kind == kind).Select(ToResource).ToList()));

        group.MapGet("/rootfolder/{id:int}", async Task<Results<JsonHttpResult<RadarrRootFolder>, NotFound>> (int id, RootFolderService roots, CancellationToken ct) =>
        {
            var root = await roots.GetAsync(id, ct);
            return root is null || root.Kind != kind ? TypedResults.NotFound() : RadarrCompat.Ok(ToResource(root));
        });
    }

    private static RadarrRootFolder ToResource(RootFolderResponse root) => new(root.Id, root.Path, root.Accessible, root.FreeSpace, []);
}
