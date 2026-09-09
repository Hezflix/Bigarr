// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Common/Extensions/IEnumerableExtensions.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: trimmed to the single member the parser uses; class made internal.
// See VENDORED.md.

using System.Collections.Generic;
using System.Linq;

namespace Bigarr.Parsing.Internal
{
    internal static class EnumerableExtensions
    {
        public static bool Empty<TSource>(this IEnumerable<TSource> source)
        {
            return !source.Any();
        }
    }
}
