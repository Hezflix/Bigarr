using System.Globalization;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Infrastructure.Auth.External;

/// <summary>
/// Plex's PIN flow, the one Overseerr and Tautulli use: no client secret, no redirect address to register. Bigarr
/// creates a PIN on plex.tv, the browser opens plex.tv's auth page with the PIN's code, Bigarr reads the PIN until it
/// carries a token, then asks plex.tv whose token it is and which servers it reaches, the way Overseerr checks access:
/// the account has access when plex.tv lists a Plex server set up in Bigarr among its resources. The token is used for
/// those two questions and dropped, never stored.
/// </summary>
public sealed class PlexIdentityProvider(
    PlexTvClient plexTv,
    PlexClient plex,
    PlexClientIdentity identity,
    BigarrDbContext db,
    TimeProvider time,
    ILogger<PlexIdentityProvider> logger) : IExternalIdentityProvider
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
        // The same identifier that created the PIN, so plex.tv's page approves the PIN Bigarr reads back.
        var clientIdentifier = await identity.GetAsync(ct);
        return new ExternalChallenge(pin.Id.ToString(CultureInfo.InvariantCulture), AuthUrl(pin.Code, clientIdentifier), null, expiresAt);
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
                : new ExternalPoll(
                    ExternalPollStatus.Completed,
                    new ExternalIdentity(account.Id, account.Username, account.Email, account.Thumb, await ServerAccessAsync(pin.AuthToken, ct)));
        }
        catch (MediaServerException ex)
        {
            throw new ExternalIdentityException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Whether the account behind <paramref name="token"/> reaches one of the Plex servers set up in Bigarr: false when
    /// none is set up, null when it cannot be told (no server gave its id, or plex.tv did not answer the resources).
    /// </summary>
    private async Task<bool?> ServerAccessAsync(string token, CancellationToken ct)
    {
        var servers = await db.MediaServers.AsNoTracking()
            .Where(s => s.Enabled && s.Kind == MediaServerKind.Plex && s.Token != string.Empty)
            .ToListAsync(ct);
        if (servers.Count == 0)
        {
            return false;
        }

        var machineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in servers)
        {
            try
            {
                if (await plex.MachineIdentifierAsync(server, ct) is { Length: > 0 } id)
                {
                    machineIds.Add(id);
                }
            }
            catch (MediaServerException ex)
            {
                logger.LogWarning("Plex {Server} did not give its id, so access to it cannot be checked: {Error}", server.Name, ex.Message);
            }
        }

        if (machineIds.Count == 0)
        {
            return null;
        }

        try
        {
            var reachable = await plexTv.ServersAsync(token, ct);
            return reachable.Any(machineIds.Contains);
        }
        catch (MediaServerException ex)
        {
            logger.LogWarning("plex.tv did not list the account's servers: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>plex.tv's auth page for <paramref name="code"/>, naming Bigarr (this install's identifier) as the app asking.</summary>
    public static string AuthUrl(string code, string clientIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(clientIdentifier);

        var query = string.Join('&', new[]
        {
            ("clientID", clientIdentifier),
            ("code", code),
            ("context[device][product]", PlexTvClient.Product),
            ("context[device][version]", Features.Server.SystemEndpoints.Version),
            ("context[device][platform]", "Web"),
            ("context[device][device]", PlexTvClient.Product),
        }.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
        return AuthPage + "?" + query;
    }
}
