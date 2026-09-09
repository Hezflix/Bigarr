using System.Security.Cryptography;
using System.Text;

namespace Bigarr.Api.Infrastructure.Auth;

public static class ApiKeyHasher
{
    private const string Prefix = "bgr_";

    public const int DisplayPrefixLength = 12;

    /// <summary>Generates a new key: <c>bgr_</c> + 32 random bytes, base64url.</summary>
    public static string Generate() =>
        Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string DisplayPrefix(string key) =>
        key.Length <= DisplayPrefixLength ? key : key[..DisplayPrefixLength];

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
