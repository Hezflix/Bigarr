// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/Model/SeriesTitleInfo.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

namespace Bigarr.Parsing.Episodes.Model
{
    public class SeriesTitleInfo
    {
        public string Title { get; set; }
        public string TitleWithoutYear { get; set; }
        public int Year { get; set; }
        public string[] AllTitles { get; set; }
    }
}
