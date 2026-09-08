// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Qualities/QualityFinder.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System.Linq;
using Bigarr.Parsing.Internal;

namespace Bigarr.Parsing.Qualities
{
    public static class QualityFinder
    {
        private static readonly ParserLog Logger = ParserLog.For(typeof(QualityFinder));

        public static Quality FindBySourceAndResolution(QualitySource source, int resolution, Modifier modifier)
        {
            // Check for a perfect 3-way match
            var matchingQuality = Quality.All.SingleOrDefault(q => q.Source == source && q.Resolution == resolution && q.Modifier == modifier);

            if (matchingQuality != null)
            {
                return matchingQuality;
            }

            // Check for Source and Modifier Match for Qualities with Unknown Resolution
            var matchingQualitiesUnknownResolution = Quality.All.Where(q => q.Source == source && (q.Resolution == 0) && q.Modifier == modifier && q != Quality.Unknown).ToList();

            if (matchingQualitiesUnknownResolution.Any())
            {
                if (matchingQualitiesUnknownResolution.Count == 1)
                {
                    return matchingQualitiesUnknownResolution.First();
                }

                foreach (var quality in matchingQualitiesUnknownResolution)
                {
                    if (quality.Source >= source)
                    {
                        Logger.Warn("Unable to find exact quality for {0},  {1}, and {2}. Using {3} as fallback", source, resolution, modifier, quality);
                        return quality;
                    }
                }
            }

            // Check for Modifier match
            var matchingModifier = Quality.All.Where(q => q.Modifier == modifier);

            var matchingResolution = matchingModifier.Where(q => q.Resolution == resolution)
                                            .OrderBy(q => q.Source)
                                            .ToList();

            var nearestQuality = Quality.Unknown;

            foreach (var quality in matchingResolution)
            {
                if (quality.Source >= source)
                {
                    nearestQuality = quality;
                    break;
                }
            }

            Logger.Warn("Unable to find exact quality for {0},  {1}, and {2}. Using {3} as fallback", source, resolution, modifier, nearestQuality);

            return nearestQuality;
        }
    }
}
