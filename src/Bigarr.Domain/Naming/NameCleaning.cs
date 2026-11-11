using System.Text.RegularExpressions;

namespace Bigarr.Domain.Naming;

/// <summary>
/// What a colon becomes in a name. Radarr's and Sonarr's <c>ColonReplacementFormat</c>, same names and same ids
/// (Radarr 5 stores 0 to 4; <see cref="Custom"/> is Sonarr 4's alone, so the film settings refuse it).
/// </summary>
public enum ColonReplacement
{
    /// <summary><c>CSI: Vegas</c> → <c>CSI Vegas</c>.</summary>
    Delete = 0,

    /// <summary><c>CSI: Vegas</c> → <c>CSI- Vegas</c>, <c>CSI:Vegas</c> → <c>CSI-Vegas</c>.</summary>
    Dash = 1,

    /// <summary><c>CSI: Vegas</c> → <c>CSI - Vegas</c>, <c>CSI:Vegas</c> → <c>CSI -Vegas</c>.</summary>
    SpaceDash = 2,

    /// <summary><c>CSI: Vegas</c> → <c>CSI - Vegas</c>, <c>CSI:Vegas</c> → <c>CSI - Vegas</c>.</summary>
    SpaceDashSpace = 3,

    /// <summary>A colon followed by a space becomes " - ", any other colon "-" (the default of both apps for a new install).</summary>
    Smart = 4,

    /// <summary>The text of <see cref="NameCleaning.CustomColon"/> (Sonarr 4).</summary>
    Custom = 5,
}

/// <summary>
/// Radarr's and Sonarr's "Replace Illegal Characters" and "Colon Replacement" naming settings. On, an illegal character
/// becomes what the two apps make of it (<c>/</c> and <c>\</c> → <c>+</c>, <c>?</c> → <c>!</c>, <c>*</c> → <c>-</c>,
/// <c>&lt; &gt; | "</c> dropped, the colon as <see cref="Colon"/> says); off, they are all dropped, the colon too.
/// </summary>
/// <param name="CustomColon">What <see cref="ColonReplacement.Custom"/> prints for a colon; never holds an illegal character.</param>
public sealed record NameCleaning(bool ReplaceIllegalCharacters = true, ColonReplacement Colon = ColonReplacement.Smart, string CustomColon = "")
{
    /// <summary>Both apps' defaults (a new Radarr or Sonarr): replace on, smart colon.</summary>
    public static readonly NameCleaning Default = new();

    /// <summary>Why <paramref name="custom"/> cannot stand for a colon: Sonarr's <c>IllegalColonCharactersValidator</c>. Empty when it can.</summary>
    public static IReadOnlyList<string> ValidateCustomColon(string? custom)
    {
        if (string.IsNullOrEmpty(custom))
        {
            return [];
        }

        var found = NameCleaner.IllegalCharacters.Append(":").Where(c => custom.Contains(c, StringComparison.Ordinal)).ToList();
        if (custom.Any(char.IsControl))
        {
            found.Add("a control character");
        }

        return found.Count == 0 ? [] : [$"The custom colon replacement contains illegal characters: {string.Join(' ', found)}."];
    }
}

/// <summary>
/// Radarr's <c>FileNameBuilder</c> cleaning, ported: a value is cleaned where it is printed (<see cref="Clean"/>), and
/// the name it ends up in is finished as the app finishes it (repeated separators merged, trailing dots and spaces cut,
/// reserved Windows device names defused). Films and series differ only in the last step, as in the two apps.
/// </summary>
public static partial class NameCleaner
{
    /// <summary>Private-use stand-in for a literal "...", which the repeated-separator merge would otherwise shrink (Sonarr's <c>{ellipsis}</c>).</summary>
    public const char Ellipsis = '';

    private static readonly string[] Bad = ["\\", "/", "<", ">", "?", "*", "|", "\""];

    private static readonly string[] Good = ["+", "+", "", "", "!", "-", "", ""];

    /// <summary>The characters Radarr and Sonarr treat as illegal (not the colon, which has its own setting).</summary>
    public static IReadOnlyList<string> IllegalCharacters => Bad;

    /// <summary>
    /// <c>FileNameBuilder.CleanFileName(name, namingConfig)</c>: the colon, then the other illegal characters, then the
    /// leading dots and spaces and the trailing spaces. Control characters are dropped as well (they are not valid in any name).
    /// </summary>
    public static string Clean(string name, NameCleaning? cleaning = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        cleaning ??= NameCleaning.Default;
        var replace = cleaning.ReplaceIllegalCharacters;
        var result = name;

        if (replace)
        {
            if (cleaning.Colon == ColonReplacement.Smart)
            {
                // A colon followed by a space reads best as " - ".
                result = result.Replace(": ", " - ", StringComparison.Ordinal).Replace(":", "-", StringComparison.Ordinal);
            }
            else
            {
                var replacement = cleaning.Colon switch
                {
                    ColonReplacement.Dash => "-",
                    ColonReplacement.SpaceDash => " -",
                    ColonReplacement.SpaceDashSpace => " - ",
                    ColonReplacement.Custom => cleaning.CustomColon,
                    _ => string.Empty,
                };
                result = result.Replace(":", replacement, StringComparison.Ordinal);
            }
        }
        else
        {
            result = result.Replace(":", string.Empty, StringComparison.Ordinal);
        }

        for (var i = 0; i < Bad.Length; i++)
        {
            result = result.Replace(Bad[i], replace ? Good[i] : string.Empty, StringComparison.Ordinal);
        }

        if (result.Any(char.IsControl))
        {
            result = new string(result.Where(c => !char.IsControl(c)).ToArray());
        }

        return result.TrimStart(' ', '.').TrimEnd(' ');
    }

    /// <summary>A file name: <c>BuildFileName</c>'s last steps. Radarr and Sonarr cut every trailing separator (<c>- ._</c>), not just dots and spaces.</summary>
    public static string FinishFile(string rendered, NameCleaning? cleaning = null) =>
        Finalize(TrimSeparators().Replace(Merge(Clean(rendered, cleaning).Trim()), string.Empty));

    /// <summary>A folder name (<c>GetMovieFolder</c>, <c>GetSeriesFolder</c>): <c>CleanFolderName</c>, which cuts dots and spaces only.</summary>
    public static string FinishFolder(string rendered, NameCleaning? cleaning = null) =>
        Finalize(Merge(Clean(rendered, cleaning)).Trim(' ', '.'));

    /// <summary>A season folder (<c>GetSeasonFolder</c>): as a folder, and a trailing or leading dash or underscore goes too.</summary>
    public static string FinishSeasonFolder(string rendered, NameCleaning? cleaning = null) =>
        Finalize(Merge(Clean(rendered, cleaning)).Trim(' ', '.').Trim(' ', '.', '-', '_'));

    /// <summary>Same sanitising for a name nobody built from a template (a folder typed by hand, a release title).</summary>
    public static string Sanitize(string name, NameCleaning? cleaning = null) => FinishFolder(name, cleaning);

    /// <summary>Radarr's <c>FileNameCleanupRegex</c>: two or more of the same separator (<c>- ._</c>) in a row become one.</summary>
    private static string Merge(string text) => RepeatedSeparator().Replace(text, m => m.Value[..1]);

    private static string Finalize(string name)
    {
        // "Con.Game" → "Con_Game": a Windows device name followed by a dot cannot be a file name.
        name = ReservedDeviceName().Replace(name, m => m.Value.Replace('.', '_'));
        name = name.Replace(Ellipsis.ToString(), "...", StringComparison.Ordinal);
        return name.Length == 0 ? "_" : name;
    }

    [GeneratedRegex(@"([- ._])(\1)+")]
    private static partial Regex RepeatedSeparator();

    [GeneratedRegex(@"[- ._]+$")]
    private static partial Regex TrimSeparators();

    [GeneratedRegex(@"^(?:aux|com[1-9]|con|lpt[1-9]|nul|prn)\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceName();
}
