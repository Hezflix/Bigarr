namespace Bigarr.Api.Features.Migration;

/// <summary>Small helpers every *arr import and follow needs: paths as the *arrs write them, dates, blank text.</summary>
public static class ArrImportHelpers
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>Trailing separators dropped; a Windows drive root keeps its backslash.</summary>
    public static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var trimmed = path.Trim();
        var stripped = trimmed.TrimEnd(Separators);
        return stripped.Length == 0 ? "/" : stripped.Length == 2 && stripped[1] == ':' ? stripped + '\\' : stripped;
    }

    /// <summary>The last folder or file name of a path; null for a blank path or a root.</summary>
    public static string? LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd(Separators);
        var cut = trimmed.LastIndexOfAny(Separators);
        var segment = cut < 0 ? trimmed : trimmed[(cut + 1)..];
        return segment.Length == 0 ? null : segment;
    }

    /// <summary>The part of an absolute file path below the item's folder, when the *arr gave no relative path.</summary>
    public static string? RelativeTo(string? filePath, string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var folder = folderPath.TrimEnd(Separators);
            if (filePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && filePath.Length > folder.Length && Separators.Contains(filePath[folder.Length]))
            {
                return filePath[(folder.Length + 1)..];
            }
        }

        return LastSegment(filePath);
    }

    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
