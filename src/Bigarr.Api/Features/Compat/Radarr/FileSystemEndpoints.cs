using Bigarr.Api.Infrastructure.Files;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bigarr.Api.Features.Compat.Radarr;

/// <summary><c>GET /filesystem?path=</c>: the folder browser behind Bazarr's path mapping screen (§7.3). Folders only unless asked.</summary>
public static class FileSystemEndpoints
{
    public static void MapCompatFileSystem(this RouteGroupBuilder group)
    {
        group.MapGet("/filesystem", Results<JsonHttpResult<RadarrFileSystem>, JsonHttpResult<RadarrError>> (string? path, bool includeFiles = false, bool allowFoldersWithoutTrailingSlashes = true) =>
        {
            var result = FileSystemBrowser.List(path, includeFiles);
            if (!result.IsSuccess)
            {
                return RadarrCompat.Error(result.Failure!.StatusCode, result.Failure.Title);
            }

            var listing = result.Value!;
            // Without a path Radarr answers with the mounts, typed as drives and without a trailing separator.
            var mounts = listing.Path.Length == 0;
            var directories = listing.Directories
                .Select(d => new RadarrFileSystemEntry(mounts ? "drive" : "folder", d.Name, mounts ? d.Path : WithSeparator(d.Path), 0, d.LastModified))
                .ToList();
            var files = listing.Files.Select(f => new RadarrFileSystemEntry("file", f.Name, f.Path, f.Size, f.LastModified)).ToList();
            var parent = listing.Parent is null ? null : WithSeparator(listing.Parent);

            return RadarrCompat.Ok(new RadarrFileSystem(parent, directories, files));
        });
    }

    /// <summary>Radarr ends folder paths with the separator; Bazarr appends names to them.</summary>
    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
