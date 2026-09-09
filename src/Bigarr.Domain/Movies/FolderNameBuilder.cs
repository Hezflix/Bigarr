using System.Text;

namespace Bigarr.Domain.Movies;

/// <summary>
/// Default folder template <c>{Title} ({Year})</c>, sanitized for every filesystem we care about.
/// The full naming template grammar arrives with the media management slice (M5).
/// </summary>
public static class FolderNameBuilder
{
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Build(string title, int? year)
    {
        var name = year is > 0 ? $"{title} ({year})" : title;
        return Sanitize(name);
    }

    public static string Sanitize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // "Mission: Impossible" → "Mission - Impossible", like Radarr's default.
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (ch == ':')
            {
                var nextIsSpace = i + 1 < name.Length && name[i + 1] == ' ';
                sb.Append(nextIsSpace ? " -" : " - ");
            }
            else if (Array.IndexOf(Invalid, ch) >= 0 || char.IsControl(ch))
            {
                continue;
            }
            else
            {
                sb.Append(ch);
            }
        }

        var collapsed = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var trimmed = collapsed.TrimEnd('.', ' ');
        return trimmed.Length == 0 ? "_" : trimmed;
    }
}
