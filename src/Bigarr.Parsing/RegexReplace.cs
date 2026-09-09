// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Parser/RegexReplace.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System.Text.RegularExpressions;

namespace Bigarr.Parsing
{
    public class RegexReplace
    {
        private readonly Regex _regex;
        private readonly string _replacementFormat;
        private readonly MatchEvaluator _replacementFunc;

        public RegexReplace(string pattern, string replacement, RegexOptions regexOptions)
        {
            _regex = new Regex(pattern, regexOptions);
            _replacementFormat = replacement;
        }

        public RegexReplace(string pattern, MatchEvaluator replacement, RegexOptions regexOptions)
        {
            _regex = new Regex(pattern, regexOptions);
            _replacementFunc = replacement;
        }

        public string Replace(string input)
        {
            if (_replacementFunc != null)
            {
                return _regex.Replace(input, _replacementFunc);
            }
            else
            {
                return _regex.Replace(input, _replacementFormat);
            }
        }

        public bool TryReplace(ref string input)
        {
            var result = _regex.IsMatch(input);
            if (_replacementFunc != null)
            {
                input = _regex.Replace(input, _replacementFunc);
            }
            else
            {
                input = _regex.Replace(input, _replacementFormat);
            }

            return result;
        }

        public override string ToString()
        {
            return _regex.ToString();
        }
    }
}
