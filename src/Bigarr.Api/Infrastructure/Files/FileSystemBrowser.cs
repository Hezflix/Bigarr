namespace Bigarr.Api.Infrastructure.Files;

/// <summary>One entry of a folder listing: a mount point, a sub-folder or a file.</summary>
public sealed record FileSystemItem(string Name, string Path, long Size, DateTime? LastModified);

/// <summary>What a folder holds, as the path pickers read it. <see cref="Path"/> is empty for the mount list.</summary>
public sealed record FolderListing(string Path, string? Parent, bool Exists, List<FileSystemItem> Directories, List<FileSystemItem> Files);

/// <summary>Reads folders for the path pickers: the web UI's and Radarr's <c>/filesystem</c>.</summary>
public static class FileSystemBrowser
{
    /// <summary>The mount points a browse starts from when no path is given.</summary>
    public static List<FileSystemItem> Mounts()
    {
        var mounts = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileSystemItem(d.Name, d.RootDirectory.FullName, 0, null))
            .DistinctBy(d => d.Path)
            .ToList();
        if (mounts.Count == 0)
        {
            mounts.Add(new FileSystemItem("/", "/", 0, null));
        }

        return mounts;
    }

    /// <summary>
    /// The folder at <paramref name="path"/>, or a failure. With <paramref name="climb"/> a path that does not exist
    /// falls back to its deepest existing ancestor, which is what a picker wants while a path is still being typed.
    /// </summary>
    public static Result<FolderListing> List(string? path, bool includeFiles = false, bool climb = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<FolderListing>.Ok(new FolderListing(string.Empty, null, false, Mounts(), []));
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Failure(StatusCodes.Status400BadRequest, "Invalid path");
        }

        var exists = Directory.Exists(full);
        var target = full;
        if (!exists)
        {
            if (!climb)
            {
                return new Failure(StatusCodes.Status404NotFound, $"Folder not found: {full}");
            }

            var ancestor = DeepestExisting(full);
            if (ancestor is null)
            {
                return Result<FolderListing>.Ok(new FolderListing(string.Empty, null, false, Mounts(), []));
            }

            target = ancestor;
        }

        try
        {
            var directory = new DirectoryInfo(target);
            var directories = directory.EnumerateDirectories()
                .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FileSystemItem(d.Name, d.FullName, 0, d.LastWriteTimeUtc))
                .ToList();
            var files = includeFiles
                ? directory.EnumerateFiles()
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FileSystemItem(f.Name, f.FullName, f.Length, f.LastWriteTimeUtc))
                    .ToList()
                : [];

            return Result<FolderListing>.Ok(new FolderListing(directory.FullName, directory.Parent?.FullName, exists, directories, files));
        }
        catch (DirectoryNotFoundException)
        {
            return new Failure(StatusCodes.Status404NotFound, $"Folder not found: {target}");
        }
        catch (UnauthorizedAccessException)
        {
            return new Failure(StatusCodes.Status403Forbidden, $"Access denied: {target}");
        }
    }

    /// <summary>The closest ancestor of <paramref name="path"/> that exists, or null when even the mount is gone.</summary>
    private static string? DeepestExisting(string path)
    {
        var current = Directory.GetParent(path);
        while (current is not null && !current.Exists)
        {
            current = current.Parent;
        }

        return current?.FullName;
    }
}
