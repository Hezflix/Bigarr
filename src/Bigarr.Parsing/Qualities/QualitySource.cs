// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Qualities/QualitySource.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

namespace Bigarr.Parsing.Qualities
{
    public enum QualitySource
    {
        UNKNOWN = 0,
        CAM,
        TELESYNC,
        TELECINE,
        WORKPRINT,
        DVD,
        TV,
        WEBDL,
        WEBRIP,
        BLURAY
    }
}
