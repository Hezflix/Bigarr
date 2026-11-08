using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bigarr.Api.Tests.Hosting;

/// <summary>The SPA fallback matches every non-API path: static assets must still be served as files.</summary>
public sealed class StaticAssetsTests : IDisposable
{
    private const string Script = "console.log('bigarr')";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("bigarr-webroot-").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public StaticAssetsTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Bigarr</title><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), Script);
        // What the web build writes next to each asset: the same bytes, compressed once.
        File.WriteAllBytes(Path.Combine(_webRoot, "assets", "app.js.gz"), Gzip(Script));
        File.WriteAllBytes(Path.Combine(_webRoot, "assets", "app.js.br"), Brotli(Script));

        var configDir = Directory.CreateTempSubdirectory("bigarr-static-cfg-").FullName;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(_webRoot);
            builder.UseSetting("Bigarr:ConfigDirectory", configDir);
            builder.UseSetting("Bigarr:InitialAdminPassword", "static-test-123");
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        try
        {
            Directory.Delete(_webRoot, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task Assets_are_served_as_files_and_unknown_routes_fall_back_to_the_spa()
    {
        var client = _factory.CreateClient();

        var asset = await client.GetAsync("/assets/app.js");
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Contains("javascript", asset.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Script, await asset.Content.ReadAsStringAsync());
        Assert.Empty(asset.Content.Headers.ContentEncoding);

        var route = await client.GetAsync("/movies/42");
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Equal("text/html", route.Content.Headers.ContentType!.MediaType);

        var api = await client.GetAsync("/api/v1/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
    }

    [Theory]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("gzip, deflate, br", "br")]
    public async Task A_client_that_asks_gets_the_precompressed_file_with_the_type_of_the_original(string acceptEncoding, string expected)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/assets/app.js");
        request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Contains("javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        await using var body = await response.Content.ReadAsStreamAsync();
        await using Stream decoded = expected == "br" ? new BrotliStream(body, CompressionMode.Decompress) : new GZipStream(body, CompressionMode.Decompress);
        Assert.Equal(Script, await new StreamReader(decoded).ReadToEndAsync());
    }

    [Fact]
    public async Task Hashed_assets_are_cached_for_good_and_the_page_is_revalidated()
    {
        var client = _factory.CreateClient();

        var asset = await client.GetAsync("/assets/app.js");
        Assert.True(asset.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl.MaxAge);
        Assert.Contains("immutable", asset.Headers.CacheControl.ToString(), StringComparison.Ordinal);

        foreach (var path in new[] { "/", "/index.html", "/movies/42" })
        {
            var page = await client.GetAsync(path);
            Assert.True(page.Headers.CacheControl!.NoCache, path);
        }
    }

    private static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static byte[] Brotli(string text)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
        {
            brotli.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }
}
