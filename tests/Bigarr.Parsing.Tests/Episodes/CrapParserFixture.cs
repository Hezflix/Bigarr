// Vendored from Sonarr develop (c018578e4b79ee048575c02d7a8aa2b929e4b837)
// Original path: src/NzbDrone.Core.Test/ParserTests/CrapParserFixture.cs — GPL-3.0
// Sonarr is Copyright (C) Sonarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using System.Text;
using FluentAssertions;

namespace Bigarr.Parsing.Episodes.Tests
{
    public class CrapParserFixture
    {
        [Theory]
        [InlineData("76El6LcgLzqb426WoVFg1vVVVGx4uCYopQkfjmLe")]
        [InlineData("Vrq6e1Aba3U amCjuEgV5R2QvdsLEGYF3YQAQkw8")]
        [InlineData("TDAsqTea7k4o6iofVx3MQGuDK116FSjPobMuh8oB")]
        [InlineData("yp4nFodAAzoeoRc467HRh1mzuT17qeekmuJ3zFnL")]
        [InlineData("oxXo8S2272KE1 lfppvxo3iwEJBrBmhlQVK1gqGc")]
        [InlineData("dPBAtu681Ycy3A4NpJDH6kNVQooLxqtnsW1Umfiv")]
        [InlineData("password - \"bdc435cb-93c4-4902-97ea-ca00568c3887.337\" yEnc")]
        [InlineData("185d86a343e39f3341e35c4dad3f9959")]
        [InlineData("ba27283b17c00d01193eacc02a8ba98eeb523a76")]
        [InlineData("45a55debe3856da318cc35882ad07e43cd32fd15")]
        [InlineData("86420f8ee425340d8894bf3bc636b66404b95f18")]
        [InlineData("ce39afb7da6cf7c04eba3090f0a309f609883862")]
        [InlineData("THIS SHOULD NEVER PARSE")]
        [InlineData("Vh1FvU3bJXw6zs8EEUX4bMo5vbbMdHghxHirc.mkv")]
        [InlineData("0e895c37245186812cb08aab1529cf8ee389dd05.mkv")]
        [InlineData("08bbc153931ce3ca5fcafe1b92d3297285feb061.mkv")]
        [InlineData("185d86a343e39f3341e35c4dad3ff159")]
        [InlineData("ah63jka93jf0jh26ahjas961.mkv")]
        [InlineData("qrdSD3rYzWb7cPdVIGSn4E7")]
        [InlineData("QZC4HDl7ncmzyUj9amucWe1ddKU1oFMZDd8r0dEDUsTd")]
        [InlineData("abc.xyz.af6021c37f7852")]
        [InlineData("e096aeb3c2c0483a96f5b32fc6d10ff5")]
        [InlineData("_unpack e096aeb3c2c0483a96f5b32fc6d10ff5.mkv")]
        public void should_not_parse_crap(string title)
        {
            Parser.ParseTitle(title).Should().BeNull();
        }

        [Theory]
        [InlineData("علم نف) أ.دعادل الأبيض ٢٠٢٤ ٣ ٣")]
        [InlineData("ror-240618_1007-1022-")]
        public void should_parse_unknown_formats_without_error(string title)
        {
            Parser.ParseTitle(title).Should().NotBeNull();
        }

        [Fact]
        public void should_not_parse_md5()
        {
            var hash = "CRAPPY TEST SEED";

            var hashAlgo = System.Security.Cryptography.MD5.Create();

            var repetitions = 100;
            var success = 0;
            for (var i = 0; i < repetitions; i++)
            {
                var hashData = hashAlgo.ComputeHash(System.Text.Encoding.Default.GetBytes(hash));

                hash = BitConverter.ToString(hashData).Replace("-", "");

                if (Parser.ParseTitle(hash) == null)
                {
                    success++;
                }
            }

            success.Should().Be(repetitions);
        }

        [Theory]
        [InlineData(32)]
        [InlineData(40)]
        public void should_not_parse_random(int length)
        {
            var charset = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            var hashAlgo = new Random();

            var repetitions = 500;
            var success = 0;
            for (var i = 0; i < repetitions; i++)
            {
                var hash = new StringBuilder(length);

                for (var x = 0; x < length; x++)
                {
                    hash.Append(charset[hashAlgo.Next() % charset.Length]);
                }

                if (Parser.ParseTitle(hash.ToString()) == null)
                {
                    success++;
                }
            }

            success.Should().Be(repetitions);
        }

        [Theory]
        [InlineData("theseriestitle1618finale")]
        public void should_not_parse_file_name_without_proper_spacing(string fileName)
        {
            Parser.ParseTitle(fileName).Should().BeNull();
        }

        [Theory]
        [InlineData("Series Title (2018) Complete 360p HDTV AAC H.264-NEXT")]
        public void should_not_parse_invalid_release_name(string fileName)
        {
            Parser.ParseTitle(fileName).Should().BeNull();
        }

        [Theory]
        [InlineData("Specials/Series - Episode Title (part 1)")]
        public void should_not_parse_special_with_part_number(string fileName)
        {
            Parser.ParseTitle(fileName).Should().BeNull();
        }
    }
}
