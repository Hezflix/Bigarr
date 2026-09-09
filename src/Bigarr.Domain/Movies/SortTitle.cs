using System.Text;

namespace Bigarr.Domain.Movies;

public static class SortTitle
{
    private static readonly string[] LeadingArticles =
        ["the ", "a ", "an ", "le ", "la ", "les ", "l'", "der ", "die ", "das ", "el ", "los ", "las ", "un ", "une "];

    /// <summary>Lowercase, leading article removed, punctuation stripped: what the library sorts on.</summary>
    public static string Build(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var lower = title.Trim().ToLowerInvariant();
        foreach (var article in LeadingArticles)
        {
            if (lower.StartsWith(article, StringComparison.Ordinal) && lower.Length > article.Length)
            {
                lower = lower[article.Length..];
                break;
            }
        }

        var sb = new StringBuilder(lower.Length);
        var lastWasSpace = true;
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? title.Trim().ToLowerInvariant() : result;
    }
}
