using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Tests.Auth;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Hosting;

/// <summary>JSON is compressed when the client asks, never otherwise, and never the responses that must stay as they are.</summary>
public sealed class ResponseCompressionTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("gzip, deflate, br")]
    public async Task The_library_list_is_compressed_when_the_client_asks_and_decodes_to_the_same_json(string acceptEncoding)
    {
        var session = await SessionAsync(factory);
        var plain = await GetRawAsync(session, "/api/v1/qualityprofiles", null);
        var compressed = await GetRawAsync(session, "/api/v1/qualityprofiles", acceptEncoding);

        var expected = acceptEncoding.Contains("br", StringComparison.Ordinal) ? "br" : "gzip";
        Assert.Equal(expected, Assert.Single(compressed.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", compressed.Headers.Vary);
        Assert.Equal("application/json", compressed.Content.Headers.ContentType!.MediaType);
        Assert.Equal(await plain.Content.ReadAsStringAsync(), await DecodeAsync(compressed));
    }

    [Fact]
    public async Task Without_accept_encoding_the_body_stays_plain()
    {
        var session = await SessionAsync(factory);
        var response = await GetRawAsync(session, "/api/v1/qualityprofiles", null);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task The_radarr_compatible_list_is_compressed_for_a_client_that_asks_and_plain_for_one_that_does_not()
    {
        var session = await SessionAsync(factory);
        var (seerr, _) = await KeyClientAsync(factory, session, "compression-seerr", "default");

        var plain = await GetRawAsync(seerr, "/api/v3/movie", null);
        var gzip = await GetRawAsync(seerr, "/api/v3/movie", "gzip");

        Assert.Empty(plain.Content.Headers.ContentEncoding);
        Assert.Equal("gzip", Assert.Single(gzip.Content.Headers.ContentEncoding));
        Assert.Equal(await plain.Content.ReadAsStringAsync(), await DecodeAsync(gzip));
    }

    [Fact]
    public async Task A_created_api_key_is_never_compressed()
    {
        var session = await SessionAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apikeys") { Content = JsonContent.Create(new { name = "breach", compatView = "default" }) };
        request.Headers.AcceptEncoding.ParseAdd("gzip, br");
        var response = await session.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Contains("\"key\":\"bgr_", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_signalr_negotiate_still_works_for_a_client_that_accepts_compression()
    {
        var session = await SessionAsync(factory);
        var (bazarr, key) = await KeyClientAsync(factory, session, "compression-bazarr", "all", header: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/signalr/messages/negotiate?negotiateVersion=1&access_token={key}");
        request.Headers.AcceptEncoding.ParseAdd("gzip, br");
        var response = await bazarr.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("connectionToken", out _));
    }

    private static async Task<HttpResponseMessage> GetRawAsync(HttpClient client, string url, string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptEncoding is not null)
        {
            request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);
        }

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static async Task<string> DecodeAsync(HttpResponseMessage response)
    {
        await using var body = await response.Content.ReadAsStreamAsync();
        await using Stream decoded = response.Content.Headers.ContentEncoding.Single() == "br"
            ? new BrotliStream(body, CompressionMode.Decompress)
            : new GZipStream(body, CompressionMode.Decompress);
        using var reader = new StreamReader(decoded);
        return await reader.ReadToEndAsync();
    }
}
