using System.Text;

namespace Bigarr.Api.Infrastructure.Downloads.Torrents;

/// <summary>
/// One shape for the info hash wherever it comes from: 40 lower-case hex characters. Indexers send it in hex, in
/// base32 (magnet style), and some Torznab definitions hex-encode the hex once more (80 characters).
/// </summary>
public static class InfoHashes
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = value.Trim();
        if (hash.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
        {
            hash = hash["urn:btih:".Length..];
        }

        if (hash.Length == 40 && hash.All(Uri.IsHexDigit))
        {
            return hash.ToLowerInvariant();
        }

        if (hash.Length == 32)
        {
            return MagnetLink.TryGetInfoHash($"magnet:?xt=urn:btih:{hash}");
        }

        if (hash.Length == 80 && hash.All(Uri.IsHexDigit))
        {
            // Hex of the ASCII hex: decode once and look again.
            var decoded = Encoding.ASCII.GetString(Convert.FromHexString(hash));
            return decoded.Length == 40 && decoded.All(Uri.IsHexDigit) ? decoded.ToLowerInvariant() : null;
        }

        return null;
    }
}
