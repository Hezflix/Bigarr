// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core.Test/ParserTests/CrapParserFixture.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: NUnit -> xUnit v2, namespaces, CoreTest base removed. See VENDORED.md

using System;
using System.Text;
using FluentAssertions;

namespace Bigarr.Parsing.Tests
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
        public void should_not_parse_crap(string title)
        {
            Parser.ParseMovieTitle(title).Should().BeNull();
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
                var hashData = hashAlgo.ComputeHash(Encoding.Default.GetBytes(hash));

                hash = BitConverter.ToString(hashData).Replace("-", "");

                if (Parser.ParseMovieTitle(hash) == null)
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

                if (Parser.ParseMovieTitle(hash.ToString()) == null)
                {
                    success++;
                }
            }

            success.Should().Be(repetitions);
        }

        [Theory]
        [InlineData("thebiggestmovie1618finale")]
        public void should_not_parse_file_name_without_proper_spacing(string fileName)
        {
            Parser.ParseMovieTitle(fileName).Should().BeNull();
        }

        [Theory]
        [InlineData("Big Movie (S01E18) Complete 360p HDTV AAC H.264-NEXT")]
        public void should_not_parse_invalid_release_name(string fileName)
        {
            Parser.ParseMovieTitle(fileName).Should().BeNull();
        }
    }
}
