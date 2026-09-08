// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/Model/ParsedMovieInfo.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System.Collections.Generic;
using Bigarr.Parsing.Languages;
using Bigarr.Parsing.Qualities;

namespace Bigarr.Parsing.Model
{
    public class ParsedMovieInfo
    {
        public ParsedMovieInfo()
        {
            MovieTitles = new List<string>();
            Languages = new List<Language>();
        }

        public List<string> MovieTitles { get; set; }
        public string OriginalTitle { get; set; }
        public string ReleaseTitle { get; set; }
        public string SimpleReleaseTitle { get; set; }
        public QualityModel Quality { get; set; }
        public List<Language> Languages { get; set; }
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public string Edition { get; set; }
        public int Year { get; set; }
        public string ImdbId { get; set; }
        public int TmdbId { get; set; }
        public string HardcodedSubs { get; set; }

        public string MovieTitle => PrimaryMovieTitle;

        public string PrimaryMovieTitle
        {
            get
            {
                if (MovieTitles.Count > 0)
                {
                    return MovieTitles[0];
                }

                return null;
            }
        }

        public override string ToString()
        {
            return string.Format("{0} - {1} {2}", PrimaryMovieTitle, Year, Quality);
        }
    }
}
