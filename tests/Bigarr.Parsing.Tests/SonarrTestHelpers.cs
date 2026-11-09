// Test helpers for the fixtures vendored from Sonarr (see src/Bigarr.Parsing/VENDORED.md).
// `XmlCleaner.ReplaceUnicode` is copied from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837),
// src/NzbDrone.Core/Indexers/XmlCleaner.cs — GPL-3.0, Copyright (C) Sonarr contributors.
// `Expand` replaces the Expansive package Sonarr's DailyEpisodeParserFixture uses for `{name}` templates.

using System.Text.RegularExpressions;

namespace Bigarr.Parsing.Tests
{
    internal static class XmlCleaner
    {
        private static readonly Regex ReplaceUnicodeRegex = new Regex(@"[^\x09\x0A\x0D -퟿-�]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ReplaceUnicode(string content)
        {
            return ReplaceUnicodeRegex.Replace(content, string.Empty);
        }
    }

    internal static class ExpansiveExtensions
    {
        public static string Expand(this string template, object values)
        {
            foreach (var property in values.GetType().GetProperties())
            {
                template = template.Replace("{" + property.Name + "}", property.GetValue(values)?.ToString());
            }

            return template;
        }
    }
}
