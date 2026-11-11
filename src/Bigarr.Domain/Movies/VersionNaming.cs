using Bigarr.Domain.Common;

namespace Bigarr.Domain.Movies;

/// <summary>Automatic version names (blueprint §3.3): resolution for scanned files, profile slug for the compat layer.</summary>
public static class VersionNaming
{
    public const string Fallback = "default";

    public static string FromQuality(int resolution, string? qualityName)
    {
        if (resolution > 0)
        {
            return $"{resolution}p";
        }

        var slug = Slug.Normalize(qualityName ?? string.Empty);
        return slug.Length == 0 || slug == "unknown" ? Fallback : slug;
    }

    public static string FromProfile(string profileName)
    {
        var slug = Slug.Normalize(profileName);
        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>A series profile's name without its leading "Series" word: <c>Series 1080p</c> gives the version <c>1080p</c>.</summary>
    public static string FromSeriesProfile(string profileName)
    {
        var slug = FromProfile(profileName);
        return slug.StartsWith("series-", StringComparison.Ordinal) && slug.Length > "series-".Length ? slug["series-".Length..] : slug;
    }

    /// <summary>Appends <c>-2</c>, <c>-3</c>… until the name is free among <paramref name="existing"/>.</summary>
    public static string Unique(string baseName, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.Ordinal);
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
