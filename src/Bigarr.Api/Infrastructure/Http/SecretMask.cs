using Bigarr.Api.Infrastructure;

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

    /// <summary>
    /// Whether two addresses are the same destination for a secret: same scheme, host and port. The path may differ
    /// (another API path on the same server). An address that cannot be parsed never equals another.
    /// </summary>
    public static bool SameDestination(string? stored, string? requested)
    {
        var storedEmpty = string.IsNullOrWhiteSpace(stored);
        var requestedEmpty = string.IsNullOrWhiteSpace(requested);
        if (storedEmpty || requestedEmpty)
        {
            return storedEmpty && requestedEmpty;
        }

        if (!Uri.TryCreate(stored!.Trim(), UriKind.Absolute, out var from) || !Uri.TryCreate(requested!.Trim(), UriKind.Absolute, out var to))
        {
            return false;
        }

        return from.Scheme == to.Scheme
            && string.Equals(from.IdnHost, to.IdnHost, StringComparison.OrdinalIgnoreCase)
            && from.Port == to.Port;
    }

    /// <summary>The refusal for a request that kept the mask but moved the secret to another address.</summary>
    public static Failure TypeAgain(string secret, string address) =>
        Failure.Invalid($"Type the {secret} again", $"The {address} changed: the stored {secret} is only kept for the address it was saved with.");
}
