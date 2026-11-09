// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/SeasonNumbersFormatter.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

using System.Collections.Generic;
using System.Linq;

namespace Bigarr.Parsing.Episodes
{
    public static class SeasonNumbersFormatter
    {
        public static string Format(IEnumerable<int> seasonNumbers, string numberFormat = "0")
        {
            var seasons = seasonNumbers.Distinct().Order().ToList();

            if (seasons.Count == 1)
            {
                return $"Season {seasons[0].ToString(numberFormat)}";
            }

            if (seasons.Last() - seasons.First() + 1 == seasons.Count)
            {
                return $"Seasons {seasons.First().ToString(numberFormat)}-{seasons.Last().ToString(numberFormat)}";
            }

            return $"Seasons {string.Join(", ", seasons.Select(s => s.ToString(numberFormat)))}";
        }
    }
}
