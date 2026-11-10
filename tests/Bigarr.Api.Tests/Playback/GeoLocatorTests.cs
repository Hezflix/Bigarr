using System.Net;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.MediaServers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Playback;

/// <summary>
/// Addresses placed on demand through a fake plex.tv: once per public address, never for a local one, and a plex.tv
/// outage is not hammered.
/// </summary>
public sealed class GeoLocatorTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 3, 14, 20, 0, 0, TimeSpan.Zero));
    private ServiceProvider _services = null!;
    private FakePlexTv _plexTv = null!;
    private GeoLocator _geo = null!;
    private int _serverId;

    public async Task InitializeAsync()
    {
        _plexTv = await FakePlexTv.StartAsync();
        var services = new ServiceCollection();
        services.AddHttpClient<PlexTvClient>(http => http.BaseAddress = new Uri(_plexTv.Url + "/"));
        services.AddScoped(_ => _database.CreateContext());
        _services = services.BuildServiceProvider();
        _geo = new GeoLocator(_services.GetRequiredService<IServiceScopeFactory>(), _services, _time, NullLogger<GeoLocator>.Instance);

        await using var db = _database.CreateContext();
        var server = new MediaServer { Name = "Plex", Url = "http://127.0.0.1:9", Token = FakePlex.Token };
        db.MediaServers.Add(server);
        await db.SaveChangesAsync();
        _serverId = server.Id;
    }

    [Fact]
    public async Task A_public_address_is_asked_once_and_a_local_one_never()
    {
        Assert.Equal(new PlaybackLocation(false, "Lyon", "Auvergne-Rhône-Alpes", "France", "FR"), await ResolveAsync("198.51.100.7"));
        Assert.Equal(new PlaybackLocation(false, "Lyon", "Auvergne-Rhône-Alpes", "France", "FR"), await ResolveAsync("198.51.100.7"));
        Assert.Equal(1, _plexTv.Lookups("198.51.100.7"));

        // plex.tv's "Unknown" answer: no place, and it is remembered too.
        Assert.Null(await ResolveAsync("192.0.2.10"));
        Assert.Null(await ResolveAsync("192.0.2.10"));
        Assert.Equal(1, _plexTv.Lookups("192.0.2.10"));

        Assert.Equal(PlaybackLocation.LocalNetwork, await ResolveAsync("192.168.1.40"));
        Assert.Equal(PlaybackLocation.LocalNetwork, await _geo.ResolveAsync("198.51.100.7", local: true, _serverId, CancellationToken.None));
        Assert.Null(await ResolveAsync(null));
        Assert.DoesNotContain(_plexTv.Requests, r => r.Query.Contains("192.168.1.40", StringComparison.Ordinal));

        // After a while the answer is asked again.
        _time.Advance(GeoLocator.Remember);
        await ResolveAsync("198.51.100.7");
        Assert.Equal(2, _plexTv.Lookups("198.51.100.7"));
    }

    [Fact]
    public async Task Plex_tv_down_gives_no_place_and_is_not_asked_again_for_a_while()
    {
        _plexTv.Down = true;
        Assert.Null(await ResolveAsync("203.0.113.20"));
        Assert.Null(await ResolveAsync("203.0.113.20"));
        Assert.Equal(1, _plexTv.Lookups("203.0.113.20"));

        _plexTv.Down = false;
        _time.Advance(GeoLocator.RetryAfterFailure);
        Assert.Equal(new PlaybackLocation(false, "Porto", "Porto", "Portugal", "PT"), await ResolveAsync("203.0.113.20"));
    }

    [Fact]
    public void Local_addresses_are_told_from_public_ones()
    {
        foreach (var local in new[] { "10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.0.10", "127.0.0.1", "169.254.1.1", "100.64.0.1", "::1", "fe80::1", "fd00::1", "::ffff:192.168.1.2" })
        {
            Assert.True(GeoLocator.IsLocalAddress(IPAddress.Parse(local)), local);
        }

        foreach (var outside in new[] { "192.0.2.10", "198.51.100.7", "203.0.113.20", "172.32.0.1", "2001:db8::1" })
        {
            Assert.False(GeoLocator.IsLocalAddress(IPAddress.Parse(outside)), outside);
        }
    }

    private Task<PlaybackLocation?> ResolveAsync(string? ip) => _geo.ResolveAsync(ip, null, _serverId, CancellationToken.None);

    public async Task DisposeAsync() => await _plexTv.DisposeAsync();

    public void Dispose()
    {
        _database.Dispose();
        _services.Dispose();
    }
}
