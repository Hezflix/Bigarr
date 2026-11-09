// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core/Parser/IsoLanguages.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md (section "Episode parser")

using System;
using System.Collections.Generic;
using System.Linq;
using Bigarr.Parsing.Internal;
using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing.Episodes
{
    public static class IsoLanguages
    {
        private static readonly HashSet<IsoLanguage> All = new HashSet<IsoLanguage>
                                                           {
                                                               new IsoLanguage("en", "", "eng", Language.English),
                                                               new IsoLanguage("fr", "fr", "fra", Language.French),
                                                               new IsoLanguage("es", "", "spa", Language.Spanish),
                                                               new IsoLanguage("de", "de", "deu", Language.German),
                                                               new IsoLanguage("it", "", "ita", Language.Italian),
                                                               new IsoLanguage("da", "", "dan", Language.Danish),
                                                               new IsoLanguage("nl", "", "nld", Language.Dutch),
                                                               new IsoLanguage("ja", "", "jpn", Language.Japanese),
                                                               new IsoLanguage("is", "", "isl", Language.Icelandic),
                                                               new IsoLanguage("zh", "cn", "zho", Language.Chinese),
                                                               new IsoLanguage("ru", "", "rus", Language.Russian),
                                                               new IsoLanguage("pl", "", "pol", Language.Polish),
                                                               new IsoLanguage("vi", "", "vie", Language.Vietnamese),
                                                               new IsoLanguage("sv", "", "swe", Language.Swedish),
                                                               new IsoLanguage("no", "", "nor", Language.Norwegian),
                                                               new IsoLanguage("nb", "", "nob", Language.Norwegian), // Norwegian Bokmål
                                                               new IsoLanguage("fi", "", "fin", Language.Finnish),
                                                               new IsoLanguage("tr", "", "tur", Language.Turkish),
                                                               new IsoLanguage("pt", "pt", "por", Language.Portuguese),
                                                               new IsoLanguage("nl", "", "nld", Language.Flemish),
                                                               new IsoLanguage("el", "", "ell", Language.Greek),
                                                               new IsoLanguage("ko", "", "kor", Language.Korean),
                                                               new IsoLanguage("hu", "", "hun", Language.Hungarian),
                                                               new IsoLanguage("he", "", "heb", Language.Hebrew),
                                                               new IsoLanguage("lt", "", "lit", Language.Lithuanian),
                                                               new IsoLanguage("cs", "", "ces", Language.Czech),
                                                               new IsoLanguage("ar", "", "ara", Language.Arabic),
                                                               new IsoLanguage("hi", "", "hin", Language.Hindi),
                                                               new IsoLanguage("bg", "", "bul", Language.Bulgarian),
                                                               new IsoLanguage("ml", "", "mal", Language.Malayalam),
                                                               new IsoLanguage("uk", "", "ukr", Language.Ukrainian),
                                                               new IsoLanguage("sk", "", "slk", Language.Slovak),
                                                               new IsoLanguage("th", "th", "tha", Language.Thai),
                                                               new IsoLanguage("pt", "br", "por", Language.PortugueseBR),
                                                               new IsoLanguage("es", "mx", "spa", Language.SpanishLatino),
                                                               new IsoLanguage("ro", "", "ron", Language.Romanian),
                                                               new IsoLanguage("lv", "", "lav", Language.Latvian),
                                                               new IsoLanguage("fa", "", "fas", Language.Persian),
                                                               new IsoLanguage("ca", "", "cat", Language.Catalan),
                                                               new IsoLanguage("hr", "", "hrv", Language.Croatian),
                                                               new IsoLanguage("sr", "", "srp", Language.Serbian),
                                                               new IsoLanguage("bs", "", "bos", Language.Bosnian),
                                                               new IsoLanguage("et", "", "est", Language.Estonian),
                                                               new IsoLanguage("ta", "", "tam", Language.Tamil),
                                                               new IsoLanguage("id", "", "ind", Language.Indonesian),
                                                               new IsoLanguage("mk", "", "mkd", Language.Macedonian),
                                                               new IsoLanguage("sl", "", "slv", Language.Slovenian),
                                                               new IsoLanguage("az", "", "aze", Language.Azerbaijani),
                                                               new IsoLanguage("uz", "", "uzb", Language.Uzbek),
                                                               new IsoLanguage("ms", "", "msa", Language.Malay),
                                                               new IsoLanguage("ur", "", "urd", Language.Urdu),
                                                               new IsoLanguage("rm", "", "roh", Language.Romansh),
                                                               new IsoLanguage("ka", "", "kat", Language.Georgian),
                                                               new IsoLanguage("ga", "", "gle", Language.Irish)
                                                           };

        private static readonly Dictionary<string, Language> AlternateIsoCodeMappings = new Dictionary<string, Language>
        {
            { "cze", Language.Czech },
            { "dut", Language.Dutch },
            { "mac", Language.Macedonian },
            { "rum", Language.Romanian },
            { "yue", Language.Chinese },
            { "zhtw", Language.Chinese }
        };

        public static IsoLanguage Find(string isoCode)
        {
            var isoArray = isoCode.Split('-');
            var langCode = isoArray[0].ToLower();

            if (langCode.Length == 2)
            {
                // Lookup ISO639-1 code
                var isoLanguages = All.Where(l => l.TwoLetterCode == langCode).ToList();

                if (isoArray.Length > 1)
                {
                    isoLanguages = isoLanguages.Any(l => l.CountryCode == isoArray[1].ToLower()) ?
                        isoLanguages.Where(l => l.CountryCode == isoArray[1].ToLower()).ToList() :
                        isoLanguages.Where(l => string.IsNullOrEmpty(l.CountryCode)).ToList();
                }

                return isoLanguages.FirstOrDefault();
            }
            else if (AlternateIsoCodeMappings.TryGetValue(isoCode, out var alternateLanguage))
            {
                return Get(alternateLanguage);
            }
            else if (langCode.Length == 3)
            {
                // Lookup ISO639-2T code
                if (Iso639BTMap.Map.TryGetValue(langCode, out var mapped))
                {
                    langCode = mapped;
                }

                return All.FirstOrDefault(l => l.ThreeLetterCode == langCode);
            }

            return null;
        }

        public static IsoLanguage Get(Language language)
        {
            return All.FirstOrDefault(l => l.Language == language);
        }

        public static IsoLanguage FindByName(string name)
        {
            return All.FirstOrDefault(l => l.Language.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
