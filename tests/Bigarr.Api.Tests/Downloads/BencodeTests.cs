using System.Security.Cryptography;
using System.Text;
using Bigarr.Api.Infrastructure.Downloads.Torrents;

namespace Bigarr.Api.Tests.Downloads;

public sealed class BencodeTests
{
    /// <summary>A minimal single-file torrent; the info dictionary is what the hash covers.</summary>
    public static byte[] Torrent(string name = "The.Matrix.1999.1080p.BluRay.x264-GRP.mkv")
    {
        static string B(string value) => $"{value.Length}:{value}";
        var info = $"d{B("length")}i12345e{B("name")}{B(name)}{B("piece length")}i16384e{B("pieces")}{B(new string('A', 20))}e";
        return Encoding.ASCII.GetBytes($"d{B("announce")}{B("http://tracker.invalid/announce")}{B("comment")}{B("test")}{B("info")}{info}e");
    }

    [Fact]
    public void Info_hash_is_the_sha1_of_the_info_dictionary()
    {
        var torrent = Torrent();
        var text = Encoding.ASCII.GetString(torrent);
        var start = text.IndexOf("4:info", StringComparison.Ordinal) + "4:info".Length;
        var info = torrent[start..^1];
        var expected = Convert.ToHexStringLower(SHA1.HashData(info));

        Assert.Equal(expected, Bencode.TryGetInfoHash(torrent));
        Assert.Equal("The.Matrix.1999.1080p.BluRay.x264-GRP.mkv", Bencode.TryGetName(torrent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>login</body></html>")]
    [InlineData("d4:infoi5e")]
    [InlineData("d8:announce3:abce")]
    [InlineData("li1ei2ee")]
    public void Anything_else_has_no_info_hash(string body)
    {
        Assert.Null(Bencode.TryGetInfoHash(Encoding.ASCII.GetBytes(body)));
        Assert.Null(Bencode.TryGetName(Encoding.ASCII.GetBytes(body)));
    }

    [Fact]
    public void Magnet_links_carry_the_hash_in_hex_or_base32()
    {
        const string hex = "0123456789abcdef0123456789abcdef01234567";
        Assert.Equal(hex, MagnetLink.TryGetInfoHash($"magnet:?xt=urn:btih:{hex.ToUpperInvariant()}&dn=The.Matrix"));
        Assert.Equal(hex, MagnetLink.TryGetInfoHash($"magnet:?dn=x&xt=urn%3Abtih%3A{hex}"));

        // The same 20 bytes in base32.
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(hex, MagnetLink.TryGetInfoHash($"magnet:?xt=urn:btih:{Base32(bytes)}"));

        Assert.Null(MagnetLink.TryGetInfoHash("magnet:?xt=urn:btih:tooshort"));
        Assert.Null(MagnetLink.TryGetInfoHash("magnet:?dn=only-a-name"));
        Assert.Null(MagnetLink.TryGetInfoHash("https://tracker.invalid/download/1.torrent"));
        Assert.Null(MagnetLink.TryGetInfoHash(null));
        Assert.True(MagnetLink.IsMagnet("MAGNET:?xt=urn:btih:abc"));
        Assert.Equal($"magnet:?xt=urn:btih:{hex}&dn=The%20Matrix", MagnetLink.Build(hex, "The Matrix"));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData(" urn:btih:0123456789abcdef0123456789abcdef01234567 ", "0123456789abcdef0123456789abcdef01234567")]
    // Some Torznab definitions hex-encode the hex once more; seen on a real tracker through Prowlarr.
    [InlineData("64373933396364646235313230343166623662613533643062336265303138323338373239396564", "d7939cddb512041fb6ba53d0b3be0182387299ed")]
    [InlineData("not-a-hash", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Info_hashes_are_normalised_to_forty_hex_characters(string? value, string? expected) => Assert.Equal(expected, InfoHashes.Normalize(value));

    [Fact]
    public void Base32_hashes_are_decoded()
    {
        const string hex = "0123456789abcdef0123456789abcdef01234567";
        Assert.Equal(hex, InfoHashes.Normalize(Base32(Convert.FromHexString(hex))));
    }

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        return output.ToString();
    }
}
