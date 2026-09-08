// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Common/Extensions/PathExtensions.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: trimmed to the two members QualityParser uses; class made internal.
// See VENDORED.md.

using System;
using System.IO;

namespace Bigarr.Parsing.Internal
{
    internal static class PathExtensions
    {
        public static string GetPathExtension(this string path)
        {
            var idx = path.LastIndexOf('.');
            if (idx == -1 || idx == path.Length - 1)
            {
                return string.Empty;
            }

            return path.Substring(idx);
        }

        public static bool ContainsInvalidPathChars(this string text)
        {
            if (text.IsNullOrWhiteSpace())
            {
                throw new ArgumentNullException(nameof(text));
            }

            return text.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
        }
    }
}
