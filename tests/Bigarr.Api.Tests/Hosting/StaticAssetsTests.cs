using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bigarr.Api.Tests.Hosting;

/// <summary>The SPA fallback matches every non-API path: static assets must still be served as files.</summary>
public sealed class StaticAssetsTests : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("bigarr-webroot-").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public StaticAssetsTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Bigarr</title><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log('bigarr')");

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
        Assert.Equal("console.log('bigarr')", await asset.Content.ReadAsStringAsync());

        var route = await client.GetAsync("/movies/42");
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Equal("text/html", route.Content.Headers.ContentType!.MediaType);

        var api = await client.GetAsync("/api/v1/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
    }
}
