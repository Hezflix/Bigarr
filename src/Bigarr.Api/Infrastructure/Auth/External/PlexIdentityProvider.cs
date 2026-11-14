using System.Globalization;
using Bigarr.Api.Infrastructure.MediaServers;

namespace Bigarr.Api.Infrastructure.Auth.External;

/// <summary>
/// Plex's PIN flow, the one Overseerr and Tautulli use: no client secret, no redirect address to register. Bigarr
/// creates a PIN on plex.tv, the browser opens plex.tv's auth page with the PIN's code, Bigarr reads the PIN until it
/// carries a token, then asks plex.tv whose token it is. The token is used for that one question and dropped.
/// </summary>
public sealed class PlexIdentityProvider(PlexTvClient plexTv, TimeProvider time) : IExternalIdentityProvider
{
    public const string ProviderKey = "plex";

    /// <summary>plex.tv's sign-in page, opened by the browser; Bigarr never calls it.</summary>
    public const string AuthPage = "https://app.plex.tv/auth/#!";

    /// <summary>How long a sign-in waits when plex.tv does not say when its PIN expires.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public string Key => ProviderKey;

    public string DisplayName => "Plex";

    public async Task<ExternalChallenge> StartAsync(CancellationToken ct)
    {
        PlexTvPin pin;
        try
        {
            pin = await plexTv.CreatePinAsync(ct);
        }
        catch (MediaServerException ex)
        {
            throw new ExternalIdentityException(ex.Message, ex);
        }

        var now = time.GetUtcNow();
        var cap = now + DefaultLifetime;
        var expiresAt = pin.ExpiresAt is { } at && at > now && at < cap ? at : cap;
        return new ExternalChallenge(pin.Id.ToString(CultureInfo.InvariantCulture), AuthUrl(pin.Code), null, expiresAt);
    }

    public async Task<ExternalPoll> PollAsync(ExternalChallenge challenge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (!long.TryParse(challenge.State, NumberStyles.None, CultureInfo.InvariantCulture, out var pinId))
        {
            return ExternalPoll.Expired;
        }

        try
        {
            var pin = await plexTv.PinAsync(pinId, ct);
            if (pin is null)
            {
                return ExternalPoll.Expired;
            }

            if (pin.AuthToken is null)
            {
                return ExternalPoll.Pending;
            }

            var account = await plexTv.AccountAsync(pin.AuthToken, ct);
            return account is null
                ? ExternalPoll.Denied
                : new ExternalPoll(ExternalPollStatus.Completed, new ExternalIdentity(account.Id, account.Username, account.Email, account.Thumb));
        }
        catch (MediaServerException ex)
        {
            throw new ExternalIdentityException(ex.Message, ex);
        }
    }

    /// <summary>plex.tv's auth page for <paramref name="code"/>, naming Bigarr as the app asking.</summary>
    public static string AuthUrl(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        var query = string.Join('&', new[]
        {
            ("clientID", PlexTvClient.ClientIdentifier),
            ("code", code),
            ("context[device][product]", PlexTvClient.Product),
            ("context[device][version]", Features.Server.SystemEndpoints.Version),
            ("context[device][platform]", "Web"),
            ("context[device][device]", PlexTvClient.Product),
        }.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
        return AuthPage + "?" + query;
    }
}
