// Adapted from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Qualities/QualityFinder.cs (+ the quality table of Qualities/Quality.cs) — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: Sonarr's algorithm is unchanged, but it runs over a copy of Sonarr's quality
// table whose rows point at Bigarr's shared quality catalogue (Radarr ids). See VENDORED.md.

using System.Collections.Generic;
using System.Linq;
using Bigarr.Parsing.Internal;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Parsing.Episodes
{
    public static class QualityFinder
    {
        private static readonly ParserLog Logger = ParserLog.For(typeof(QualityFinder));

        // Sonarr's Quality.All, in Sonarr's order: (Sonarr source, resolution) -> Bigarr quality.
        private static readonly List<(QualitySource Source, int Resolution, Quality Quality)> SonarrQualities = new()
        {
            (QualitySource.Unknown, 0, Quality.Unknown),
            (QualitySource.Television, 480, Quality.SDTV),
            (QualitySource.DVD, 480, Quality.DVD),
            (QualitySource.Web, 1080, Quality.WEBDL1080p),
            (QualitySource.Television, 720, Quality.HDTV720p),
            (QualitySource.Web, 720, Quality.WEBDL720p),
            (QualitySource.Bluray, 720, Quality.Bluray720p),
            (QualitySource.Bluray, 1080, Quality.Bluray1080p),
            (QualitySource.Web, 480, Quality.WEBDL480p),
            (QualitySource.Television, 1080, Quality.HDTV1080p),
            (QualitySource.TelevisionRaw, 1080, Quality.RAWHD),
            (QualitySource.WebRip, 480, Quality.WEBRip480p),
            (QualitySource.Bluray, 480, Quality.Bluray480p),
            (QualitySource.WebRip, 720, Quality.WEBRip720p),
            (QualitySource.WebRip, 1080, Quality.WEBRip1080p),
            (QualitySource.Television, 2160, Quality.HDTV2160p),
            (QualitySource.WebRip, 2160, Quality.WEBRip2160p),
            (QualitySource.Web, 2160, Quality.WEBDL2160p),
            (QualitySource.Bluray, 2160, Quality.Bluray2160p),
            (QualitySource.BlurayRaw, 1080, Quality.Remux1080p),
            (QualitySource.BlurayRaw, 2160, Quality.Remux2160p),
            (QualitySource.Bluray, 576, Quality.Bluray576p),
        };

        // Bigarr: the reverse lookup QualityParser needs where Sonarr reads `quality.Source` on its own
        // Quality type (the file-extension fallback). Qualities outside Sonarr's table are Unknown.
        public static QualitySource SourceOf(Quality quality)
        {
            return SonarrQualities.Where(q => q.Quality == quality)
                                  .Select(q => q.Source)
                                  .FirstOrDefault(QualitySource.Unknown);
        }

        public static Quality FindBySourceAndResolution(QualitySource source, int resolution)
        {
            var matchingQuality = SonarrQualities.Where(q => q.Source == source && q.Resolution == resolution)
                                                 .Select(q => q.Quality)
                                                 .SingleOrDefault();

            if (matchingQuality != null)
            {
                return matchingQuality;
            }

            // Handle 576p releases that have a Television or Web source, so they don't get rolled up to Bluray 576p
            if (resolution < 720)
            {
                switch (source)
                {
                    case QualitySource.Television:
                        return Quality.SDTV;
                    case QualitySource.Web:
                        return Quality.WEBDL480p;
                    case QualitySource.WebRip:
                        return Quality.WEBRip480p;
                }
            }

            var matchingResolution = SonarrQualities.Where(q => q.Resolution == resolution)
                                                    .OrderBy(q => q.Source)
                                                    .ToList();

            var nearestQuality = Quality.Unknown;

            foreach (var quality in matchingResolution)
            {
                if (quality.Source >= source)
                {
                    nearestQuality = quality.Quality;
                    break;
                }
            }

            Logger.Warn("Unable to find exact quality for {0} and {1}. Using {2} as fallback", source, resolution, nearestQuality);

            return nearestQuality;
        }
    }
}
