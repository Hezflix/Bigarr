namespace Bigarr.Api.Infrastructure.Http;

/// <summary>
/// A stored secret is never sent back: a response carries this mask in its place, and a request that sends the mask
/// back means "unchanged". An empty value clears the secret; anything else replaces it.
/// </summary>
public static class SecretMask
{
    public const string Value = "********";

    /// <summary>What a response shows for a stored secret: the mask, or null when there is none.</summary>
    public static string? Of(string? secret) => string.IsNullOrEmpty(secret) ? null : Value;

    /// <summary>Whether a request kept the stored secret (it sent the mask back).</summary>
    public static bool Kept(string? value) => value is not null && value.Trim() == Value;
}
