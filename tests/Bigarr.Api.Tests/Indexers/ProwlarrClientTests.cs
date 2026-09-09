using System.Net;
using Bigarr.Api.Infrastructure.Indexers.Prowlarr;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Indexers;

public sealed class ProwlarrClientTests
{
    private static (ProwlarrClient Client, StubHttpHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> responder, string? baseUrl = "https://prowlarr.example/", string? apiKey = "k3y")
    {
        var handler = new StubHttpHandler(responder);
        var options = Options.Create(new ProwlarrOptions { BaseUrl = baseUrl, ApiKey = apiKey });
        return (new ProwlarrClient(new StubHttpClientFactory(handler), options), handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Indexers_are_read_with_the_api_key_header()
    {
        var (client, handler) = Create(_ => Json(ProwlarrRecorded.Indexers));

        var indexers = await client.GetIndexersAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://prowlarr.example/api/v1/indexer", request.ToString());

        Assert.Equal(3, indexers.Count);
        var one = indexers[0];
        Assert.Equal(1, one.Id);
        Assert.Equal("Tracker One", one.Name);
        Assert.True(one.Enable);
        Assert.True(one.IsTorrent);
        Assert.Equal(10, one.Priority);
        Assert.Equal("private", one.Privacy);
        Assert.Equal(["q", "imdbId", "tmdbId"], one.Capabilities!.MovieSearchParams);
        Assert.Equal(2, one.Capabilities.Categories.Count);
        Assert.Equal([2040, 2045], one.Capabilities.Categories[0].SubCategories.Select(c => c.Id));

        Assert.False(indexers[1].Enable);
        Assert.False(indexers[2].IsTorrent);

        var status = await Create(_ => Json(ProwlarrRecorded.Status)).Client.GetStatusAsync(CancellationToken.None);
        Assert.Equal("Prowlarr", status.AppName);
        Assert.Equal("2.3.0.5321", status.Version);
    }

    [Fact]
    public async Task A_login_redirect_is_reported_as_an_authentication_problem_naming_the_sso_host()
    {
        var (client, _) = Create(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://sso.example/oauth2/generic?redirect_url=https%3A%2F%2Fprowlarr.example%2Fapi%2Fv1%2Findexer");
            return response;
        });

        var ex = await Assert.ThrowsAsync<IndexerException>(() => client.GetIndexersAsync(CancellationToken.None));
        Assert.Equal(IndexerFailureKind.Authentication, ex.Kind);
        Assert.Contains("sso.example", ex.Message);
        Assert.Contains("login page", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, IndexerFailureKind.Authentication)]
    [InlineData(HttpStatusCode.InternalServerError, IndexerFailureKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, IndexerFailureKind.Request)]
    public async Task Http_failures_are_typed(HttpStatusCode status, IndexerFailureKind kind)
    {
        var (client, _) = Create(_ => Json("{}", status));
        var ex = await Assert.ThrowsAsync<IndexerException>(() => client.GetIndexersAsync(CancellationToken.None));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task Html_and_missing_configuration_are_refused()
    {
        var (html, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!DOCTYPE html><html></html>", System.Text.Encoding.UTF8, "text/html") });
        Assert.Equal(IndexerFailureKind.InvalidResponse, (await Assert.ThrowsAsync<IndexerException>(() => html.GetIndexersAsync(CancellationToken.None))).Kind);

        var (unconfigured, handler) = Create(_ => Json("[]"), apiKey: null);
        var ex = await Assert.ThrowsAsync<IndexerException>(() => unconfigured.GetIndexersAsync(CancellationToken.None));
        Assert.Contains("not configured", ex.Message);
        Assert.Empty(handler.Requests);
    }
}
