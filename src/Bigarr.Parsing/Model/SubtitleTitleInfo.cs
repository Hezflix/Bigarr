// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/Model/SubtitleTitleInfo.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System.Collections.Generic;
using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing.Model
{
    public class SubtitleTitleInfo
    {
        public List<string> LanguageTags { get; set; }
        public Language Language { get; set; }
        public string RawTitle { get; set; }
        public string Title { get; set; }
        public int Copy { get; set; }
        public bool TitleFirst { get; set; }
    }
}
