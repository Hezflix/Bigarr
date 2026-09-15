using System.Security.Cryptography;
using System.Text;

namespace Bigarr.Api.Infrastructure.Downloads.Torrents;

/// <summary>
/// Just enough bencode to find the <c>info</c> dictionary of a torrent file: its SHA-1 is the info hash, the
/// identifier every torrent client reports. Nothing is materialised; the reader only tracks offsets.
/// </summary>
public static class Bencode
{
    private const int MaxDepth = 64;

    /// <summary>Lower-case hex info hash of a torrent file, or null when the bytes are not a torrent.</summary>
    public static string? TryGetInfoHash(ReadOnlySpan<byte> torrent)
    {
        if (torrent.Length < 2 || torrent[0] != (byte)'d')
        {
            return null;
        }

        try
        {
            var position = 1;
            while (position < torrent.Length && torrent[position] != (byte)'e')
            {
                var key = ReadString(torrent, ref position);
                var start = position;
                Skip(torrent, ref position, 0);
                if (key.SequenceEqual("info"u8))
                {
                    // The info value is a dictionary; anything else is not a torrent.
                    return torrent[start] == (byte)'d' ? Convert.ToHexStringLower(SHA1.HashData(torrent[start..position])) : null;
                }
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return null;
    }

    /// <summary>The <c>info.name</c> of a torrent file, when present.</summary>
    public static string? TryGetName(ReadOnlySpan<byte> torrent)
    {
        if (torrent.Length < 2 || torrent[0] != (byte)'d')
        {
            return null;
        }

        try
        {
            var position = 1;
            while (position < torrent.Length && torrent[position] != (byte)'e')
            {
                var key = ReadString(torrent, ref position);
                if (!key.SequenceEqual("info"u8))
                {
                    Skip(torrent, ref position, 0);
                    continue;
                }

                if (torrent[position] != (byte)'d')
                {
                    return null;
                }

                position++;
                while (position < torrent.Length && torrent[position] != (byte)'e')
                {
                    var infoKey = ReadString(torrent, ref position);
                    if (infoKey.SequenceEqual("name"u8))
                    {
                        return Encoding.UTF8.GetString(ReadString(torrent, ref position));
                    }

                    Skip(torrent, ref position, 1);
                }

                return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return null;
    }

    private static ReadOnlySpan<byte> ReadString(ReadOnlySpan<byte> data, ref int position)
    {
        var length = 0;
        var digits = 0;
        while (position < data.Length && data[position] is >= (byte)'0' and <= (byte)'9')
        {
            length = checked(length * 10 + (data[position] - '0'));
            position++;
            digits++;
        }

        if (digits == 0 || position >= data.Length || data[position] != (byte)':')
        {
            throw new FormatException("Expected a bencoded string.");
        }

        position++;
        if (position + length > data.Length)
        {
            throw new FormatException("Bencoded string runs past the end.");
        }

        var value = data.Slice(position, length);
        position += length;
        return value;
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (depth > MaxDepth || position >= data.Length)
        {
            throw new FormatException("Malformed bencode.");
        }

        switch (data[position])
        {
            case (byte)'i':
                var end = data[position..].IndexOf((byte)'e');
                if (end < 0)
                {
                    throw new FormatException("Unterminated integer.");
                }

                position += end + 1;
                return;

            case (byte)'l':
                position++;
                while (position < data.Length && data[position] != (byte)'e')
                {
                    Skip(data, ref position, depth + 1);
                }

                position++;
                return;

            case (byte)'d':
                position++;
                while (position < data.Length && data[position] != (byte)'e')
                {
                    ReadString(data, ref position);
                    Skip(data, ref position, depth + 1);
                }

                position++;
                return;

            case >= (byte)'0' and <= (byte)'9':
                ReadString(data, ref position);
                return;

            default:
                throw new FormatException($"Unexpected byte 0x{data[position]:x2}.");
        }
    }
}

/// <summary>The info hash carried by a magnet link (<c>xt=urn:btih:</c>, hex or base32).</summary>
public static class MagnetLink
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static bool IsMagnet(string? link) => link is not null && link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase);

    public static string? TryGetInfoHash(string? magnet)
    {
        if (!IsMagnet(magnet))
        {
            return null;
        }

        var query = magnet!.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
        {
            return null;
        }

        foreach (var pair in magnet[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || !pair[..equals].Equals("xt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(equals + 1)..]);
            if (!value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var hash = value["urn:btih:".Length..].Trim();
            if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
            {
                return hash.ToLowerInvariant();
            }

            if (hash.Length == 32)
            {
                var bytes = Base32Decode(hash.ToUpperInvariant());
                if (bytes is not null)
                {
                    return Convert.ToHexStringLower(bytes);
                }
            }
        }

        return null;
    }

    public static string Build(string infoHash, string? displayName) =>
        $"magnet:?xt=urn:btih:{infoHash}" + (string.IsNullOrWhiteSpace(displayName) ? string.Empty : $"&dn={Uri.EscapeDataString(displayName)}");

    private static byte[]? Base32Decode(string value)
    {
        var bits = 0;
        var buffer = 0;
        var output = new List<byte>(20);
        foreach (var c in value)
        {
            var index = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.Count == 20 ? output.ToArray() : null;
    }
}
