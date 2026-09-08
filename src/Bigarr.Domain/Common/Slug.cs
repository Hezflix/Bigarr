using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Bigarr.Domain.Common;

/// <summary>Short lowercase identifiers used for version names and compat views (e.g. <c>1080p</c>, <c>directors-cut</c>).</summary>
public static partial class Slug
{
    public const int MaxLength = 64;

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxLength && SlugRegex().IsMatch(value);

    /// <summary>Lowercases, strips diacritics, collapses any non [a-z0-9] run into a single dash.</summary>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length);
        var lastWasDash = true;
        foreach (var ch in value.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }

        var slug = sb.ToString().TrimEnd('-');
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
