// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/MediaFiles/FileExtensions.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: trimmed to RemoveFileExtension (the archive/dangerous/executable extension
// sets belong to the import pipeline, not the parser); class made internal. See VENDORED.md.

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Bigarr.Parsing.Internal
{
    internal static partial class FileExtensions
    {
        [GeneratedRegex(@"\.[a-z0-9]{2,4}$", RegexOptions.IgnoreCase)]
        private static partial Regex FileExtensionRegex { get; }

        private static readonly HashSet<string> UsenetExtensions = new HashSet<string>()
        {
            ".par2",
            ".nzb"
        };

        public static string RemoveFileExtension(string title)
        {
            title = FileExtensionRegex.Replace(title, m =>
            {
                var extension = m.Value.ToLower();
                if (MediaFileExtensions.Extensions.Contains(extension) || UsenetExtensions.Contains(extension))
                {
                    return string.Empty;
                }

                return m.Value;
            });

            return title;
        }
    }
}
