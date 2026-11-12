using Bigarr.Domain.Common;

namespace Bigarr.Domain.Auth;

public enum CompatViewKind
{
    /// <summary>A movie = its default version. A classic Radarr.</summary>
    Default = 0,

    /// <summary>One row per version. For Bazarr.</summary>
    All = 1,

    /// <summary>Only the version with this name; nothing for movies that lack it. For a "4K" Seerr.</summary>
    Version = 2,
}

/// <summary>What an API key sees through the Radarr-compatible API. Stored as text.</summary>
public sealed record CompatView
{
    public static readonly CompatView Default = new(CompatViewKind.Default, null);

    public static readonly CompatView All = new(CompatViewKind.All, null);

    private const string VersionPrefix = "version:";

    private CompatView(CompatViewKind kind, string? versionName)
    {
        Kind = kind;
        VersionName = versionName;
    }

    public CompatViewKind Kind { get; }

    public string? VersionName { get; }

    public static CompatView ForVersion(string versionName)
    {
        if (!Slug.IsValid(versionName))
        {
            throw new ArgumentException($"'{versionName}' is not a valid version name (lowercase slug).", nameof(versionName));
        }

        return new CompatView(CompatViewKind.Version, versionName);
    }

    public static bool TryParse(string? value, out CompatView view)
    {
        view = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        value = value.Trim();
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            view = All;
            return true;
        }

        if (value.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = value[VersionPrefix.Length..];
            if (Slug.IsValid(name))
            {
                view = new CompatView(CompatViewKind.Version, name);
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a version with this name and default flag is a line of the view.</summary>
    public bool Sees(string versionName, bool isDefault) => Kind switch
    {
        CompatViewKind.Default => isDefault,
        CompatViewKind.All => true,
        _ => string.Equals(versionName, VersionName, StringComparison.Ordinal),
    };

    public static CompatView Parse(string value) =>
        TryParse(value, out var view) ? view : throw new FormatException($"'{value}' is not a valid compat view. Expected 'default', 'all' or 'version:<slug>'.");

    public override string ToString() => Kind switch
    {
        CompatViewKind.Default => "default",
        CompatViewKind.All => "all",
        _ => VersionPrefix + VersionName,
    };
}
