using System.Net;
using System.Text;
using Bigarr.Api.Infrastructure.Downloads.QBittorrent;
using Bigarr.Api.Infrastructure.Downloads.Torrents;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Downloads;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Downloads;

/// <summary>The WebUI API v2 conversation, against a scripted qBittorrent.</summary>
public sealed class QBittorrentClientTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";

    private static DownloadClient Client(string? username = "admin") => new()
    {
        Name = "qbt",
        Host = "qbt.local",
        Port = 8080,
        UrlBase = "/qbittorrent",
        Username = username,
        Password = username is null ? null : "adminadmin",
        Category = "bigarr-movies",
    };

    [Fact]
    public async Task Test_logs_in_reads_the_version_and_creates_the_category()
    {
        var categoryCreated = false;
        var handler = new StubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/v2/auth/login", StringComparison.Ordinal))
            {
                var form = request.Content!.ReadAsStringAsync().Result;
                Assert.Contains("username=admin", form);
                Assert.Contains("password=adminadmin", form);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                response.Headers.TryAddWithoutValidation("Set-Cookie", "SID=abc123; HttpOnly; path=/");
                return response;
            }

            Assert.Equal("SID=abc123", request.Headers.GetValues("Cookie").Single());
            Assert.StartsWith("http://qbt.local:8080/qbittorrent", request.RequestUri.ToString());
            return path switch
            {
                "/qbittorrent/api/v2/app/version" => Text("v5.0.4"),
                "/qbittorrent/api/v2/app/webapiVersion" => Text("2.11.3"),
                "/qbittorrent/api/v2/torrents/categories" => Json(categoryCreated ? """{"bigarr-movies":{"name":"bigarr-movies","savePath":""}}""" : "{}"),
                "/qbittorrent/api/v2/torrents/createCategory" => Create(),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };

            HttpResponseMessage Create()
            {
                categoryCreated = true;
                return Text(string.Empty);
            }
        });

        var client = NewClient(handler);
        var info = await client.TestAsync(Client(), CancellationToken.None);

        Assert.Equal("v5.0.4", info.Version);
        Assert.Equal("2.11.3", info.ApiVersion);
        Assert.Equal("bigarr-movies", info.Category);
        Assert.True(info.CategoryCreated);
        Assert.Single(handler.Requests, r => r.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal)); // one session for the whole test

        // A second call on the same configured client reuses the session.
        await client.TestAsync(Client(), CancellationToken.None);
        Assert.Single(handler.Requests, r => r.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wrong_credentials_and_bans_are_authentication_failures()
    {
        var refused = NewClient(new StubHttpHandler(_ => Text("Fails.")));
        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => refused.TestAsync(Client(), CancellationToken.None));
        Assert.Equal(DownloadClientFailureKind.Authentication, ex.Kind);
        Assert.Contains("username or the password", ex.Message);

        var banned = NewClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        ex = await Assert.ThrowsAsync<DownloadClientException>(() => banned.TestAsync(Client(), CancellationToken.None));
        Assert.Equal(DownloadClientFailureKind.Authentication, ex.Kind);
        Assert.Contains("banned", ex.Message);

        var proxied = NewClient(new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://sso.example.com/login");
            return response;
        }));
        ex = await Assert.ThrowsAsync<DownloadClientException>(() => proxied.TestAsync(Client(username: null), CancellationToken.None));
        Assert.Equal(DownloadClientFailureKind.Authentication, ex.Kind);
        Assert.Contains("sso.example.com", ex.Message);

        var wrongPath = NewClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        ex = await Assert.ThrowsAsync<DownloadClientException>(() => wrongPath.TestAsync(Client(username: null), CancellationToken.None));
        Assert.Equal(DownloadClientFailureKind.Request, ex.Kind);
        Assert.Contains("URL base", ex.Message);
    }

    [Fact]
    public async Task An_expired_session_is_renewed_once()
    {
        var logins = 0;
        var handler = new StubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/auth/login", StringComparison.Ordinal))
            {
                logins++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok.") };
                response.Headers.TryAddWithoutValidation("Set-Cookie", $"SID=session{logins}; path=/");
                return response;
            }

            var sid = request.Headers.GetValues("Cookie").Single();
            return sid == "SID=session1" ? new HttpResponseMessage(HttpStatusCode.Forbidden) : Json("[]");
        });

        var items = await NewClient(handler).GetItemsAsync(Client(), CancellationToken.None);
        Assert.Empty(items);
        Assert.Equal(2, logins);
    }

    [Fact]
    public async Task Adding_a_magnet_posts_the_category_and_returns_the_hash()
    {
        string? addForm = null;
        var handler = new StubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/torrents/categories", StringComparison.Ordinal))
            {
                return Json("""{"bigarr-movies":{"name":"bigarr-movies","savePath":""}}""");
            }

            if (path.EndsWith("/torrents/add", StringComparison.Ordinal))
            {
                addForm = request.Content!.ReadAsStringAsync().Result;
                return Text("Ok.");
            }

            if (path.EndsWith("/torrents/info", StringComparison.Ordinal))
            {
                Assert.Contains($"hashes={Hash}", request.RequestUri.Query);
                return Json($$"""[{"hash":"{{Hash}}","name":"The.Matrix","state":"metaDL","progress":0,"size":0,"total_size":0,"category":"bigarr-movies"}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = NewClient(handler);
        var request = new DownloadRequest("The.Matrix.1999.1080p.BluRay.x264-GRP", null, $"magnet:?xt=urn:btih:{Hash.ToUpperInvariant()}&dn=The.Matrix", null, 12_000_000_000, SeedRatio: 1.5, SeedTime: TimeSpan.FromHours(48));
        var id = await client.AddAsync(Client(username: null), request, CancellationToken.None);

        Assert.Equal(Hash, id);
        Assert.NotNull(addForm);
        Assert.Contains("name=urls", addForm);
        Assert.Contains("urn:btih:" + Hash.ToUpperInvariant(), addForm);
        Assert.Contains("name=category", addForm);
        Assert.Contains("bigarr-movies", addForm);
        Assert.Contains("name=ratioLimit", addForm);
        Assert.Contains("1.5", addForm);
        Assert.Contains("name=seedingTimeLimit", addForm);
        Assert.Contains("2880", addForm);
        Assert.DoesNotContain(handler.Requests, r => r.AbsolutePath.EndsWith("/auth/login", StringComparison.Ordinal)); // no credentials, no login
    }

    [Fact]
    public async Task Adding_a_torrent_link_downloads_the_file_first_and_uses_its_hash()
    {
        var torrent = BencodeTests.Torrent();
        var expected = Bencode.TryGetInfoHash(torrent)!;
        string? addForm = null;
        var handler = new StubHttpHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.StartsWith("https://tracker.invalid/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(torrent) { Headers = { ContentType = new("application/x-bittorrent") } } };
            }

            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/torrents/categories", StringComparison.Ordinal))
            {
                return Json("""{"bigarr-movies":{"name":"bigarr-movies"}}""");
            }

            if (path.EndsWith("/torrents/add", StringComparison.Ordinal))
            {
                addForm = request.Content!.ReadAsStringAsync().Result;
                return Text("Ok.");
            }

            if (path.EndsWith("/torrents/info", StringComparison.Ordinal))
            {
                return Json($$"""[{"hash":"{{expected}}","name":"x","state":"downloading","progress":0.1,"size":1,"total_size":1,"category":"bigarr-movies"}]""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = NewClient(handler);
        // The indexer's own hash is wrong on purpose: the file decides.
        var request = new DownloadRequest("The.Matrix.1999.1080p.BluRay.x264-GRP", "https://tracker.invalid/download/42.torrent", null, "ffffffffffffffffffffffffffffffffffffffff", 12_000_000_000);
        var id = await client.AddAsync(Client(username: null), request, CancellationToken.None);

        Assert.Equal(expected, id);
        Assert.Contains("name=torrents", addForm);
        Assert.Contains($"filename={expected}.torrent", addForm);
    }

    [Fact]
    public async Task A_login_page_instead_of_a_torrent_file_is_an_invalid_release()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>", Encoding.UTF8, "text/html") });
        var client = NewClient(handler);
        var request = new DownloadRequest("x", "https://tracker.invalid/download/42.torrent", null, null, 1);

        var ex = await Assert.ThrowsAsync<DownloadClientException>(() => client.AddAsync(Client(username: null), request, CancellationToken.None));
        Assert.Equal(DownloadClientFailureKind.InvalidRelease, ex.Kind);
        Assert.Contains("text/html", ex.Message);
    }

    [Fact]
    public async Task Torrents_of_the_category_are_mapped_to_queue_items()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Contains("category=bigarr-movies", request.RequestUri!.Query);
            return Json($$"""
                [
                  {"hash":"{{Hash}}","name":"The.Matrix.1999.1080p.BluRay.x264-GRP","state":"downloading","progress":0.25,"size":4000,"total_size":4000,"amount_left":3000,"dlspeed":1000,"eta":3,"category":"bigarr-movies","content_path":"/data/downloads/bigarr-movies/The.Matrix","added_on":1700000000,"completion_on":0,"ratio":0.1,"seeding_time":0,"num_seeds":7},
                  {"hash":"aaaa","name":"done","state":"stalledUP","progress":1,"size":10,"total_size":10,"amount_left":0,"dlspeed":0,"eta":8640000,"category":"bigarr-movies","completion_on":1700000500,"ratio":2.5,"seeding_time":3600},
                  {"hash":"bbbb","name":"broken","state":"missingFiles","progress":0.5,"size":10,"total_size":10,"amount_left":5,"eta":8640000,"category":"bigarr-movies"},
                  {"hash":"cccc","name":"stalled","state":"stalledDL","progress":0.5,"size":10,"total_size":10,"amount_left":5,"eta":8640000,"category":"bigarr-movies"},
                  {"hash":"dddd","name":"paused","state":"stoppedDL","progress":0.5,"size":10,"total_size":10,"amount_left":5,"eta":8640000,"category":"bigarr-movies"}
                ]
                """);
        });

        var items = await NewClient(handler).GetItemsAsync(Client(username: null), CancellationToken.None);

        Assert.Equal(5, items.Count);
        var downloading = items[0];
        Assert.Equal(Hash, downloading.DownloadId);
        Assert.Equal(DownloadItemStatus.Downloading, downloading.Status);
        Assert.Equal(0.25, downloading.Progress);
        Assert.Equal(3000, downloading.RemainingSize);
        Assert.Equal(TimeSpan.FromSeconds(3), downloading.Eta);
        Assert.Equal("/data/downloads/bigarr-movies/The.Matrix", downloading.OutputPath);
        Assert.Equal(7, downloading.Seeders);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), downloading.AddedAt);
        Assert.Null(downloading.CompletedAt);

        var done = items[1];
        Assert.Equal(DownloadItemStatus.Completed, done.Status);
        Assert.Null(done.Eta);
        Assert.Equal(2.5, done.Ratio);
        Assert.Equal(TimeSpan.FromHours(1), done.SeedingTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000500), done.CompletedAt);

        Assert.Equal(DownloadItemStatus.Failed, items[2].Status);
        Assert.Contains("cannot find the files", items[2].Message);
        Assert.Equal(DownloadItemStatus.Stalled, items[3].Status);
        Assert.Equal(DownloadItemStatus.Paused, items[4].Status);
    }

    [Theory]
    [InlineData("error", DownloadItemStatus.Failed)]
    [InlineData("pausedDL", DownloadItemStatus.Paused)]
    [InlineData("queuedDL", DownloadItemStatus.Queued)]
    [InlineData("metaDL", DownloadItemStatus.Queued)]
    [InlineData("forcedDL", DownloadItemStatus.Downloading)]
    [InlineData("uploading", DownloadItemStatus.Completed)]
    [InlineData("pausedUP", DownloadItemStatus.Completed)]
    [InlineData("moving", DownloadItemStatus.Completed)]
    [InlineData("whatever", DownloadItemStatus.Unknown)]
    public void Every_qbittorrent_state_has_a_status(string state, DownloadItemStatus expected) => Assert.Equal(expected, QBittorrentClient.StatusOf(state));

    private static QBittorrentClient NewClient(StubHttpHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        return new QBittorrentClient(factory, new TorrentFileFetcher(factory), TimeProvider.System, NullLogger<QBittorrentClient>.Instance);
    }

    private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
