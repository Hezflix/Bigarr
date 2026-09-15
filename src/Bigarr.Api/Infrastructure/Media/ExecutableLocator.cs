namespace Bigarr.Api.Infrastructure.Media;

/// <summary>Finds an external tool (ffprobe, ffmpeg): a full path is checked as is, a bare name is looked up on the PATH, then in the usual bin folders.</summary>
public static class ExecutableLocator
{
    private static readonly string[] CommonDirectories = ["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/opt/local/bin"];

    public static string? Find(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return null;
        }

        if (nameOrPath.Contains(Path.DirectorySeparatorChar) || nameOrPath.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(nameOrPath) ? Path.GetFullPath(nameOrPath) : null;
        }

        var names = OperatingSystem.IsWindows() ? new[] { nameOrPath, nameOrPath + ".exe" } : [nameOrPath];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(CommonDirectories);
        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
