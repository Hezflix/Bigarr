// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/IsoLanguagesFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using FluentAssertions;
using Bigarr.Parsing.Languages;

namespace Bigarr.Parsing.Tests
{
    public class IsoLanguagesFixture
    {
        [Theory]
        [InlineData("en")]
        [InlineData("eng")]
        [InlineData("en-US")]
        [InlineData("en-GB")]
        public void should_return_iso_language_for_English(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.English);
        }

        [Theory]
        [InlineData("enus")]
        [InlineData("enusa")]
        [InlineData("wo")]
        [InlineData("fr-CA")]
        public void unknown_or_invalid_code_should_return_null(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().Be(null);
        }

        [Theory]
        [InlineData("pt")]
        [InlineData("por")]
        [InlineData("pt-PT")]
        public void should_return_portuguese(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Portuguese);
        }

        [Theory]
        [InlineData("de-AU")]
        public void should_not_return_portuguese(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().Be(null);
        }

        [Theory]
        [InlineData("te")]
        [InlineData("tel")]
        [InlineData("te-IN")]
        public void should_return_telugu(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Telugu);
        }

        [Theory]
        [InlineData("af")]
        [InlineData("afr")]
        [InlineData("af-ZA")]
        public void should_return_afrikaans(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Afrikaans);
        }

        [Theory]
        [InlineData("mr")]
        [InlineData("mar")]
        [InlineData("mr-IN")]
        public void should_return_marathi(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Marathi);
        }

        [Theory]
        [InlineData("tl")]
        [InlineData("tgl")]
        [InlineData("tl-PH")]
        public void should_return_tagalog(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Tagalog);
        }

        [Theory]
        [InlineData("ur")]
        [InlineData("urd")]
        [InlineData("ur-PK")]
        public void should_return_urdu(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Urdu);
        }

        [Theory]
        [InlineData("rm")]
        [InlineData("roh")]
        [InlineData("rm-CH")]
        public void should_return_romansh(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Romansh);
        }

        [Theory]
        [InlineData("mn")]
        [InlineData("mon")]
        [InlineData("khk")]
        [InlineData("mvf")]
        [InlineData("mn-Cyrl")]
        public void should_return_mongolian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Mongolian);
        }

        [Theory]
        [InlineData("bn")]
        [InlineData("ben")]
        [InlineData("bn-BD")]
        [InlineData("bn-IN")]
        public void should_return_bengali(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Bengali);
        }

        [Theory]
        [InlineData("ka")]
        [InlineData("geo")]
        [InlineData("kat")]
        [InlineData("ka-GE")]
        public void should_return_georgian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Georgian);
        }
    }
}
