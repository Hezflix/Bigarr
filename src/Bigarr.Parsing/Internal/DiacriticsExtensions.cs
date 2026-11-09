// Written for Bigarr (not vendored). Sonarr's parser calls `RemoveDiacritics()`, which upstream
// delegates to the Diacritical.Net package; Bigarr.Parsing references no NuGet package, so this
// helper reproduces it on top of the FormD decomposition Radarr's `RemoveAccent` already uses,
// plus the few letters that FormD does not decompose. See VENDORED.md.

using System.Text;

namespace Bigarr.Parsing.Internal
{
    internal static class DiacriticsExtensions
    {
        public static string RemoveDiacritics(this string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var stripped = text.RemoveAccent();
            var builder = new StringBuilder(stripped.Length);

            foreach (var c in stripped)
            {
                builder.Append(c switch
                {
                    'ð' => "d",
                    'Ð' => "D",
                    'ø' => "o",
                    'Ø' => "O",
                    'đ' => "d",
                    'Đ' => "D",
                    'ł' => "l",
                    'Ł' => "L",
                    'ħ' => "h",
                    'Ħ' => "H",
                    'ı' => "i",
                    _ => c.ToString()
                });
            }

            return builder.ToString();
        }
    }
}
