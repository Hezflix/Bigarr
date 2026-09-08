// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/SceneChecker.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

namespace Bigarr.Parsing
{
    public static class SceneChecker
    {
        // This method should prefer false negatives over false positives.
        // It's better not to use a title that might be scene than to use one that isn't scene
        public static string GetSceneTitle(string title)
        {
            if (title == null)
            {
                return null;
            }

            if (!title.Contains('.'))
            {
                return null;
            }

            if (title.Contains(' '))
            {
                return null;
            }

            var parsedTitle = Parser.ParseMovieTitle(title);

            if (parsedTitle == null ||
                parsedTitle.ReleaseGroup == null ||
                parsedTitle.Quality.Quality == Qualities.Quality.Unknown ||
                string.IsNullOrWhiteSpace(parsedTitle.PrimaryMovieTitle) ||
                string.IsNullOrWhiteSpace(parsedTitle.ReleaseTitle))
            {
                return null;
            }

            return parsedTitle.ReleaseTitle;
        }

        public static bool IsSceneTitle(string title)
        {
            return GetSceneTitle(title) != null;
        }
    }
}
