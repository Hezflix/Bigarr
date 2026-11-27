using System.Net;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Downloads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Bigarr.Api.Tests.Downloads;

/// <summary>Indexer links, followed by the fetcher itself: redirects to mirrors and to magnets.</summary>
public sealed class TorrentFileFetcherTests
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Show.S01E01&tr=http%3A%2F%2Ftracker.invalid%2Fannounce";

    [Fact]
    public async Task A_plain_http_link_redirected_to_a_magnet_returns_the_magnet()
    {
        // Prowlarr answers Nyaa links this way: an http link, a 301 to the magnet.
        var handler = new StubHttpHandler(_ => Redirect(Magnet));

        var fetched = await Fetcher(handler).FetchAsync("http://prowlarr:9696/7/download?apikey=x&link=y", CancellationToken.None);

        Assert.Null(fetched.File);
        Assert.Equal(Magnet, fetched.Magnet);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Redirects_to_a_mirror_are_followed_to_the_torrent_file()
    {
        var torrent = BencodeTests.Torrent();
        var handler = new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/download/42" => Redirect("https://mirror.invalid/files/42.torrent"),
            "/files/42.torrent" => Redirect("/final/42.torrent"),
            "/final/42.torrent" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(torrent) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var fetched = await Fetcher(handler).FetchAsync("http://tracker.invalid/download/42", CancellationToken.None);

        Assert.Equal(torrent, fetched.File);
        Assert.Equal(
            ["http://tracker.invalid/download/42", "https://mirror.invalid/files/42.torrent", "https://mirror.invalid/final/42.torrent"],
            handler.Requests.Select(u => u.ToString()));
    }

    [Theory]
    [InlineData("https://tracker.invalid/download/42", "http://mirror.invalid/42.torrent")]
    [InlineData("http://tracker.invalid/download/42", "ftp://mirror.invalid/42.torrent")]
    public async Task A_redirect_to_a_downgraded_or_foreign_scheme_is_an_invalid_release(string link, string location)
    {
        var handler = new StubHttpHandler(_ => Redirect(location));

        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => Fetcher(handler).FetchAsync(link, CancellationToken.None));

        Assert.Equal(DownloadClientFailureKind.InvalidRelease, ex.Kind);
        Assert.Contains(location, ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Endless_redirects_stop_after_the_limit()
    {
        var handler = new StubHttpHandler(request => Redirect(request.RequestUri + "x"));

        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => Fetcher(handler).FetchAsync("http://tracker.invalid/a", CancellationToken.None));

        Assert.Equal(DownloadClientFailureKind.InvalidRelease, ex.Kind);
        Assert.Equal(TorrentFileFetcher.MaxRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public void The_app_handler_leaves_redirects_to_the_fetcher()
    {
        // Left to the handler, a magnet redirect from a plain http link is followed and fails on its missing host.
        using var factory = new BigarrApiFactory();
        var handlers = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler? handler = handlers.CreateHandler(TorrentFileFetcher.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
    }

    private static TorrentFileFetcher Fetcher(StubHttpHandler handler) => new(new StubHttpClientFactory(handler));

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.MovedPermanently) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
}
