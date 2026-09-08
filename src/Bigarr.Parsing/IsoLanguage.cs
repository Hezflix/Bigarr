// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/IsoLanguage.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing
{
    public class IsoLanguage
    {
        public string TwoLetterCode { get; set; }
        public string ThreeLetterCode { get; set; }
        public string CountryCode { get; set; }
        public string EnglishName { get; set; }
        public Language Language { get; set; }
        public string CountyCodeLower => CountryCode?.ToLower();

        public IsoLanguage(string twoLetterCode, string countryCode, string threeLetterCode, string englishName, Language language)
        {
            TwoLetterCode = twoLetterCode;
            ThreeLetterCode = threeLetterCode;
            CountryCode = countryCode;
            EnglishName = englishName;
            Language = language;
        }
    }
}
