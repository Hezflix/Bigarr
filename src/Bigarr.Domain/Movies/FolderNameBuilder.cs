using Bigarr.Domain.Naming;

namespace Bigarr.Domain.Movies;

/// <summary>
/// Default folder template <c>{Title} ({Year})</c>, cleaned like Radarr's folder names (<see cref="NameCleaner"/>); the
/// naming settings' "Replace Illegal Characters" and "Colon Replacement" apply when <paramref name="cleaning"/> is given.
/// </summary>
public static class FolderNameBuilder
{
    public static string Build(string title, int? year, NameCleaning? cleaning = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        var name = NameCleaner.Clean(title, cleaning);
        return NameCleaner.FinishFolder(year is > 0 ? $"{name} ({year})" : name, cleaning);
    }

    public static string Sanitize(string name, NameCleaning? cleaning = null) => NameCleaner.Sanitize(name, cleaning);
}
