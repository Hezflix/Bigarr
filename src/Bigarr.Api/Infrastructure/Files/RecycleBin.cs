namespace Bigarr.Api.Infrastructure.Files;

/// <summary>
/// Replaced and deleted files go to the recycle bin when one is configured (blueprint §3.7), keeping their path relative
/// to the root folder so they can be put back by hand. Without a bin they are deleted for good. Retention is counted from
/// the moment the file entered the bin, not from its original modification time.
/// </summary>
public sealed class RecycleBin(TimeProvider time, ILogger<RecycleBin> logger)
{
    /// <summary>Moves <paramref name="path"/> into the bin and returns its new location, or null when it was deleted outright or was already gone.</summary>
    public string? RemoveFile(string path, string? binPath, string? relativeTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(binPath))
        {
            File.Delete(path);
            logger.LogInformation("Deleted {Path} (no recycle bin configured)", path);
            return null;
        }

        var target = TargetFor(binPath, path, relativeTo);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target, overwrite: false);
        TouchSafely(target);
        logger.LogInformation("Moved {Path} to the recycle bin ({Target})", path, target);
        return target;
    }

    /// <summary>Removes a folder and everything in it, through the bin when configured.</summary>
    public string? RemoveDirectory(string path, string? binPath, string? relativeTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(binPath))
        {
            Directory.Delete(path, recursive: true);
            logger.LogInformation("Deleted folder {Path} (no recycle bin configured)", path);
            return null;
        }

        var target = TargetFor(binPath, path, relativeTo);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            Directory.Move(path, target);
        }
        catch (IOException)
        {
            // Another volume: copy then delete.
            CopyDirectory(path, target);
            Directory.Delete(path, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            TouchSafely(file);
        }

        logger.LogInformation("Moved folder {Path} to the recycle bin ({Target})", path, target);
        return target;
    }

    /// <summary>Deletes what has been in the bin longer than the retention and prunes empty folders. Returns the files deleted.</summary>
    public int Cleanup(string binPath, int retentionDays)
    {
        if (string.IsNullOrWhiteSpace(binPath) || retentionDays <= 0 || !Directory.Exists(binPath))
        {
            return 0;
        }

        var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(binPath, "*", SearchOption.AllDirectories).ToList())
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning("Could not delete {File} from the recycle bin: {Message}", file, ex.Message);
            }
        }

        PruneEmptyDirectories(binPath, keepRoot: true);
        if (deleted > 0)
        {
            logger.LogInformation("Recycle bin: {Count} file(s) older than {Days} day(s) deleted", deleted, retentionDays);
        }

        return deleted;
    }

    /// <summary>Removes the folder when nothing is left in it. Used after the last file of a version leaves.</summary>
    public static void DeleteIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
            // leave it
        }
    }

    private string TargetFor(string binPath, string path, string? relativeTo)
    {
        var relative = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(relativeTo))
        {
            var candidate = Path.GetRelativePath(relativeTo, path);
            if (!candidate.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(candidate))
            {
                relative = candidate;
            }
        }

        var target = Path.Combine(binPath, relative);
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return target;
        }

        var stamp = time.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        return Path.Combine(directory, $"{name}_{stamp}{extension}");
    }

    private void TouchSafely(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, time.GetUtcNow().UtcDateTime);
        }
        catch (IOException)
        {
            // retention then counts from the original date; acceptable
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void PruneEmptyDirectories(string path, bool keepRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            PruneEmptyDirectories(directory, keepRoot: false);
        }

        if (!keepRoot)
        {
            DeleteIfEmpty(path);
        }
    }
}
