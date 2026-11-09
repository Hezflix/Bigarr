// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/Model/ReleaseType.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")


namespace Bigarr.Parsing.Episodes.Model
{
    public enum ReleaseType
    {
        Unknown = 0,
        SingleEpisode = 1,
        MultiEpisode = 2,
        SeasonPack = 3,
        MultiSeasonPack = 4
    }
}
