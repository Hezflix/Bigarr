using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>Where a stream came from: the local network, or a place for a public address. Null fields are unknown.</summary>
public sealed record PlaybackLocation(bool Local, string? City, string? Region, string? Country, string? CountryCode)
{
    public static readonly PlaybackLocation LocalNetwork = new(true, null, null, null, null);

    public static PlaybackLocation Of(GeoLocation location) => new(false, location.City, location.Region, location.Country, location.CountryCode);
}

/// <summary>
/// Places the addresses streams come from when a page shows them, through plex.tv's geolocation with a Plex server's
/// token, like Tautulli. Nothing is stored: answers are kept in memory for <see cref="Remember"/> (a restart asks again),
/// local addresses never leave Bigarr, and a failed lookup is not retried for <see cref="RetryAfterFailure"/>, so a
/// plex.tv outage costs one timeout per address rather than one per page load.
/// </summary>
public sealed class GeoLocator(IServiceScopeFactory scopeFactory, IServiceProvider services, TimeProvider time, ILogger<GeoLocator> logger)
{
    public static readonly TimeSpan Remember = TimeSpan.FromDays(7);

    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    /// <summary>Beyond this many addresses the memory starts over: a guard, far above what a home server sees.</summary>
    public const int MaxRemembered = 10_000;

    private readonly ConcurrentDictionary<string, (GeoLocation? Location, DateTime At)> _known = new();

    private readonly ConcurrentDictionary<string, DateTime> _failed = new();

    private readonly ConcurrentDictionary<string, Lazy<Task<GeoLocation?>>> _pending = new();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Private, loopback, link-local and carrier-grade NAT addresses: nothing to place. Documentation ranges count as
    /// public, so demos and tests can use them.
    /// </summary>
    public static bool IsLocalAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || b[0] == 0;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
    }

    /// <summary>
    /// The location to show for a stream: the local network for a local address (no call), otherwise plex.tv's answer.
    /// Null when the address is missing or could not be placed.
    /// </summary>
    public async Task<PlaybackLocation?> ResolveAsync(string? ipAddress, bool? local, int serverId, CancellationToken ct)
    {
        if (local == true)
        {
            return PlaybackLocation.LocalNetwork;
        }

        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return null;
        }

        if (IsLocalAddress(address))
        {
            return PlaybackLocation.LocalNetwork;
        }

        return await LookupAsync(address, serverId, ct) is { } found ? PlaybackLocation.Of(found) : null;
    }

    /// <summary>A public address's location, null when unknown or when plex.tv cannot be asked now.</summary>
    public async Task<GeoLocation?> LookupAsync(IPAddress address, int serverId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);

        var key = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        var now = Now;
        if (_known.TryGetValue(key, out var known) && now - known.At < Remember)
        {
            return known.Location;
        }

        if (_failed.TryGetValue(key, out var failedAt) && now - failedAt < RetryAfterFailure)
        {
            return null;
        }

        // One lookup per address at a time, whoever asks; the caller's cancellation does not cancel it for the others.
        var pending = _pending.GetOrAdd(key, k => new Lazy<Task<GeoLocation?>>(() => FetchAsync(k, serverId)));
        try
        {
            return await pending.Value.WaitAsync(ct);
        }
        finally
        {
            if (pending.Value.IsCompleted)
            {
                _pending.TryRemove(new KeyValuePair<string, Lazy<Task<GeoLocation?>>>(key, pending));
            }
        }
    }

    private async Task<GeoLocation?> FetchAsync(string ipAddress, int serverId)
    {
        string? token;
        using (var scope = scopeFactory.CreateScope())
        {
            // Any Plex token works for plex.tv; the stream's own server first.
            token = await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().MediaServers.AsNoTracking()
                .Where(s => s.Kind == MediaServerKind.Plex && s.Token != string.Empty)
                .OrderByDescending(s => s.Id == serverId).ThenByDescending(s => s.Enabled)
                .Select(s => s.Token)
                .FirstOrDefaultAsync();
        }

        if (token is null)
        {
            return null;
        }

        var now = Now;
        GeoLocation? found;
        try
        {
            found = await services.GetRequiredService<PlexTvClient>().GeoIpAsync(token, ipAddress, CancellationToken.None);
        }
        catch (MediaServerException ex)
        {
            _failed[ipAddress] = now;
            logger.LogWarning("Could not place {IpAddress}: {Error}", ipAddress, ex.Message);
            return null;
        }

        _failed.TryRemove(ipAddress, out _);
        if (_known.Count >= MaxRemembered)
        {
            _known.Clear();
        }

        _known[ipAddress] = (found, now);
        return found;
    }
}
