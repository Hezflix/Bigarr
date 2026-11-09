// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/IsoLanguage.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing.Episodes
{
    public class IsoLanguage
    {
        public string TwoLetterCode { get; set; }
        public string ThreeLetterCode { get; set; }
        public string CountryCode { get; set; }
        public Language Language { get; set; }
        public string CountyCodeLower => CountryCode?.ToLower();

        public IsoLanguage(string twoLetterCode, string countryCode, string threeLetterCode, Language language)
        {
            TwoLetterCode = twoLetterCode;
            ThreeLetterCode = threeLetterCode;
            CountryCode = countryCode;
            Language = language;
        }
    }
}
