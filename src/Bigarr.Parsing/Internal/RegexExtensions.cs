// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Common/Extensions/RegexExtensions.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: class made internal. See VENDORED.md.

using System.Text.RegularExpressions;

namespace Bigarr.Parsing.Internal
{
    internal static class RegexExtensions
    {
        public static int EndIndex(this Capture regexMatch)
        {
            return regexMatch.Index + regexMatch.Length;
        }
    }
}
