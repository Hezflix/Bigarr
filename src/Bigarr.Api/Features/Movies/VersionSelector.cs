using Bigarr.Domain.Common;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Movies;

/// <summary>Which version of each film a bulk action touches. Same syntax as an API key's view: <c>default</c>, <c>all</c> or <c>version:&lt;name&gt;</c>.</summary>
public sealed record VersionSelector
{
    public static readonly VersionSelector Default = new(null, all: false);

    public static readonly VersionSelector All = new(null, all: true);

    private const string NamePrefix = "version:";

    private VersionSelector(string? name, bool all)
    {
        Name = name;
        IsAll = all;
    }

    /// <summary>Set for <c>version:&lt;name&gt;</c>.</summary>
    public string? Name { get; }

    public bool IsAll { get; }

    public static bool TryParse(string? value, out VersionSelector selector)
    {
        selector = Default;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        value = value.Trim();
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            selector = All;
            return true;
        }

        if (value.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase) && Slug.IsValid(value[NamePrefix.Length..]))
        {
            selector = new VersionSelector(value[NamePrefix.Length..], all: false);
            return true;
        }

        return false;
    }

    /// <summary>The selected versions among a film's; empty when it lacks the version.</summary>
    public List<T> Pick<T>(IEnumerable<T> versions, Func<T, bool> isDefault, Func<T, string> name)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (IsAll)
        {
            return versions.ToList();
        }

        return Name is null ? versions.Where(isDefault).Take(1).ToList() : versions.Where(v => name(v) == Name).Take(1).ToList();
    }

    /// <summary>The same choice made by the database, for actions that load only the versions they change.</summary>
    public IQueryable<MovieVersion> Filter(IQueryable<MovieVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (IsAll)
        {
            return versions;
        }

        var name = Name;
        return name is null ? versions.Where(v => v.IsDefault) : versions.Where(v => v.Name == name);
    }

    /// <summary>Why a film that lacks the version is skipped: "No 4k version".</summary>
    public string SkipReason => Name is null ? "No default version" : $"No {Name} version";

    public override string ToString() => IsAll ? "all" : Name is null ? "default" : NamePrefix + Name;
}
