// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/SceneChecker.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

namespace Bigarr.Parsing.Episodes
{
    public static class SceneChecker
    {
        // This method should prefer false negatives over false positives.
        // It's better not to use a title that might be scene than to use one that isn't scene
        public static bool IsSceneTitle(string title)
        {
            if (!title.Contains('.'))
            {
                return false;
            }

            if (title.Contains(' '))
            {
                return false;
            }

            var parsedTitle = Parser.ParseTitle(title);

            if (parsedTitle == null ||
                parsedTitle.ReleaseGroup == null ||
                parsedTitle.Quality.Quality == Qualities.Quality.Unknown ||
                string.IsNullOrWhiteSpace(parsedTitle.SeriesTitle))
            {
                return false;
            }

            return true;
        }
    }
}
