using Bigarr.Api.Infrastructure.Http;

namespace Bigarr.Api.Tests.Server;

/// <summary>A stored secret is only kept for the address it was saved with: same scheme, host and port.</summary>
public sealed class SecretMaskTests
{
    [Theory]
    [InlineData("http://indexer.local:9117/torznab", "http://indexer.local:9117/torznab")]
    [InlineData("http://indexer.local:9117/torznab", "http://indexer.local:9117/torznab/")]
    [InlineData("http://indexer.local:9117/torznab", "http://indexer.local:9117/another/api")]
    [InlineData("http://indexer.local:9117", " HTTP://INDEXER.LOCAL:9117/ ")]
    [InlineData("https://plex.local", "https://plex.local:443/")]
    [InlineData(null, null)]
    [InlineData("", " ")]
    public void The_same_scheme_host_and_port_is_the_same_destination(string? stored, string? requested) =>
        Assert.True(SecretMask.SameDestination(stored, requested));

    [Theory]
    [InlineData("http://indexer.local:9117/torznab", "http://elsewhere.local:9117/torznab")]
    [InlineData("http://indexer.local:9117/torznab", "http://indexer.local:9118/torznab")]
    [InlineData("https://indexer.local/torznab", "http://indexer.local/torznab")]
    [InlineData("http://indexer.local", "https://indexer.local")]
    [InlineData("http://indexer.local", null)]
    [InlineData(null, "http://indexer.local")]
    [InlineData("http://indexer.local", "not an address")]
    [InlineData("not an address", "not an address")]
    public void Another_scheme_host_or_port_or_an_address_that_cannot_be_read_is_another_destination(string? stored, string? requested) =>
        Assert.False(SecretMask.SameDestination(stored, requested));
}
