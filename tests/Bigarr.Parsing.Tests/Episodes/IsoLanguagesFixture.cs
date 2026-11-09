// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/IsoLanguagesFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using Bigarr.Parsing.Languages;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
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
        [InlineData("cze")]
        public void should_lookup_cze_via_alternate_iso_code_mapping(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Czech);
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
        [InlineData("ka")]
        [InlineData("geo")]
        [InlineData("kat")]
        [InlineData("ka-GE")]
        public void should_return_georgian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Georgian);
        }

        [Theory]
        [InlineData("ga")]
        [InlineData("gle")]
        [InlineData("ga-IE")]
        public void should_return_irish(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Irish);
        }
    }
}
