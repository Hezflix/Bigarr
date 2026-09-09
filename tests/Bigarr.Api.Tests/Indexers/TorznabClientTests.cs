using System.Net;
using System.Web;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Indexers;

public sealed class TorznabClientTests
{
    private static readonly MovieSearchCriteria Matrix = new("The Matrix", 1999, "tt0133093", 603, ["Matrix"]);

    private static Indexer Indexer(string baseUrl = "http://prowlarr:9696/1", string? apiKey = "k3y") => new()
    {
        Id = 1,
        Name = "Prowlarr",
        BaseUrl = baseUrl,
        ApiKey = apiKey,
        Categories = [2000, 2040],
    };

    private static (TorznabClient Client, StubHttpHandler Handler) Create(string caps, string feed, HttpStatusCode feedStatus = HttpStatusCode.OK)
    {
        var handler = new StubHttpHandler(request =>
            request.RequestUri!.Query.Contains("t=caps", StringComparison.Ordinal)
                ? StubHttpHandler.Xml(caps)
                : StubHttpHandler.Xml(feed, feedStatus));
        var client = new TorznabClient(new StubHttpClientFactory(handler), new FixedTimeProvider(DateTimeOffset.UtcNow), NullLogger<TorznabClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task Id_search_is_used_when_the_indexer_supports_it_and_capabilities_are_cached()
    {
        var (client, handler) = Create(TorznabRecorded.Caps, TorznabRecorded.Feed);

        var releases = await client.SearchAsync(Indexer(), Matrix, CancellationToken.None);
        Assert.Equal(2, releases.Count);

        Assert.Equal(2, handler.Requests.Count);
        var caps = HttpUtility.ParseQueryString(handler.Requests[0].Query);
        Assert.Equal("caps", caps["t"]);
        Assert.Equal("k3y", caps["apikey"]);
        Assert.Null(caps["cat"]);
        Assert.Equal("/1/api", handler.Requests[0].AbsolutePath);

        var search = HttpUtility.ParseQueryString(handler.Requests[1].Query);
        Assert.Equal("movie", search["t"]);
        Assert.Equal("603", search["tmdbid"]);
        Assert.Null(search["q"]);
        Assert.Equal("2000,2040", search["cat"]);
        Assert.Equal("100", search["limit"]);
        Assert.Equal("1", search["extended"]);

        await client.SearchAsync(Indexer(), Matrix, CancellationToken.None);
        Assert.Equal(3, handler.Requests.Count); // no second caps call
    }

    [Fact]
    public async Task Text_search_falls_back_to_one_query_per_title_with_the_year()
    {
        var (client, handler) = Create(TorznabRecorded.CapsTextOnly, TorznabRecorded.Feed);

        var releases = await client.SearchAsync(Indexer(), Matrix, CancellationToken.None);
        Assert.Equal(2, releases.Count); // duplicates across queries are folded by guid

        var queries = handler.Requests.Skip(1).Select(u => HttpUtility.ParseQueryString(u.Query)).ToList();
        Assert.Equal(2, queries.Count);
        Assert.All(queries, q => Assert.Equal("movie", q["t"]));
        Assert.Equal(["The Matrix 1999", "Matrix 1999"], queries.Select(q => q["q"]));
    }

    [Fact]
    public void Query_building_prefers_imdb_over_text_and_uses_the_year_parameter_when_offered()
    {
        var imdbOnly = new IndexerCapabilities(true, true, false, true, [], null);
        var query = Assert.Single(TorznabClient.BuildSearchQueries(imdbOnly, Matrix));
        Assert.Equal("0133093", query["imdbid"]);

        var textWithYear = new IndexerCapabilities(true, false, false, true, [], null);
        var first = TorznabClient.BuildSearchQueries(textWithYear, Matrix)[0];
        Assert.Equal("The Matrix", first["q"]);
        Assert.Equal("1999", first["year"]);

        var noMovieSearch = new IndexerCapabilities(false, false, false, false, [], null);
        var generic = TorznabClient.BuildSearchQueries(noMovieSearch, Matrix)[0];
        Assert.Equal("search", generic["t"]);
        Assert.Equal("The Matrix 1999", generic["q"]);

        Assert.Equal("Amelie Poulain - Le Fabuleux Destin", TorznabClient.CleanQuery("Amelie Poulain: - Le Fabuleux Destin!"));
    }

    [Fact]
    public void Urls_are_built_from_base_and_api_path_without_doubling_the_path()
    {
        var plain = TorznabClient.BuildUrl(Indexer("http://prowlarr:9696/1/"), new Dictionary<string, string> { ["t"] = "caps" }, includeCategories: false);
        Assert.StartsWith("http://prowlarr:9696/1/api?t=caps&apikey=k3y", plain);

        var withPath = TorznabClient.BuildUrl(Indexer("http://prowlarr:9696/1/api"), new Dictionary<string, string> { ["t"] = "caps" }, includeCategories: false);
        Assert.StartsWith("http://prowlarr:9696/1/api?t=caps", withPath);

        var noKey = TorznabClient.BuildUrl(Indexer(apiKey: null), new Dictionary<string, string> { ["t"] = "caps" }, includeCategories: false);
        Assert.DoesNotContain("apikey", noKey);

        Assert.Equal("http://x/api?t=movie&apikey=***&cat=2000", TorznabClient.Redact("http://x/api?t=movie&apikey=k3y&cat=2000"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, IndexerFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, IndexerFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, IndexerFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, IndexerFailureKind.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, IndexerFailureKind.ServerError)]
    [InlineData(HttpStatusCode.NotFound, IndexerFailureKind.Request)]
    public async Task Http_failures_are_typed(HttpStatusCode status, IndexerFailureKind kind)
    {
        var (client, _) = Create(TorznabRecorded.Caps, "", status);
        var ex = await Assert.ThrowsAsync<IndexerException>(() => client.SearchAsync(Indexer(), Matrix, CancellationToken.None));
        Assert.Equal(kind, ex.Kind);
    }

    [Fact]
    public async Task A_redirect_to_a_login_page_is_an_authentication_failure_not_html_to_parse()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://sso.example/oauth2/generic?redirect_url=x");
            return response;
        });
        var client = new TorznabClient(new StubHttpClientFactory(handler), TimeProvider.System, NullLogger<TorznabClient>.Instance);

        var ex = await Assert.ThrowsAsync<IndexerException>(() => client.GetCapabilitiesAsync(Indexer(), CancellationToken.None));
        Assert.Equal(IndexerFailureKind.Authentication, ex.Kind);
        Assert.Contains("sso.example", ex.Message);
    }

    [Fact]
    public async Task Network_errors_are_unreachable()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new TorznabClient(new StubHttpClientFactory(handler), TimeProvider.System, NullLogger<TorznabClient>.Instance);

        var ex = await Assert.ThrowsAsync<IndexerException>(() => client.GetCapabilitiesAsync(Indexer(), CancellationToken.None));
        Assert.Equal(IndexerFailureKind.Unreachable, ex.Kind);
        Assert.Contains("connection refused", ex.Message);
    }

    [Fact]
    public async Task Recent_feed_uses_movie_mode_when_available()
    {
        var (client, handler) = Create(TorznabRecorded.Caps, TorznabRecorded.Feed);
        var recent = await client.FetchRecentAsync(Indexer(), CancellationToken.None);

        Assert.Equal(2, recent.Count);
        var query = HttpUtility.ParseQueryString(handler.Requests[1].Query);
        Assert.Equal("movie", query["t"]);
        Assert.Null(query["q"]);
    }
}
