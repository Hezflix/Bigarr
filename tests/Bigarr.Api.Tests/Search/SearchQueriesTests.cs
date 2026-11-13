using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Indexers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Search;

/// <summary>
/// The advanced view of the interactive search: what each indexer was asked, step by step, with the outcome, and never
/// a secret. Torznab is answered by a stub that records the real query strings, so the report is checked against them.
/// </summary>
public sealed class SearchQueriesTests
{
    private const string ApiKey = "s3cr3t-k3y";

    [Fact]
    public async Task A_film_search_reports_each_indexers_requests_redacted_only_when_asked()
    {
        var torznab = new StubHttpHandler(request => request.RequestUri!.AbsolutePath.StartsWith("/down", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : StubHttpHandler.Xml(request.RequestUri.Query.Contains("t=caps", StringComparison.Ordinal) ? TorznabRecorded.Caps : TorznabRecorded.Feed));
        using var app = new TorznabFactory(torznab, series: false);
        var client = await ClientAsync(app);
        var (hd, _) = await ProfilesAsync(client);
        var movie = await AddMovieAsync(client, 603, hd); // The Matrix
        var demo = await AddTorznabAsync(client, "Demo Torznab", "http://demo:p4ssw0rd@indexer.invalid/1");
        var down = await AddTorznabAsync(client, "Down Torznab", "http://indexer.invalid/down");

        // The normal path is unchanged: no report.
        var plain = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
        Assert.Null(plain.Queries);
        torznab.Requests.Clear();

        // The down indexer failed the plain search and is paused: asked again, it is reported as skipped.
        var response = await client.GetAsync($"/api/v1/search?movieId={movie.Id}&includeQueries=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain("p4ssw0rd", body, StringComparison.Ordinal);
        var result = (await response.Content.ReadFromJsonAsync<SearchResponse>(Json))!;
        Assert.Equal(2, result.Queries!.Count);

        // Capabilities were cached by the plain search: one request, by the film's TMDB id, as the stub saw it.
        var report = result.Queries.Single(q => q.IndexerId == demo.Id);
        var sent = Assert.Single(torznab.Requests);
        var request = Assert.Single(report.Requests);
        Assert.Equal("IDs", request.Step);
        Assert.Equal("movie", request.Function);
        Assert.Equal("603", request.Parameters["tmdbid"]);
        Assert.Equal(string.Join(',', Indexer.DefaultCategories), request.Parameters["cat"]);
        Assert.DoesNotContain("apikey", request.Parameters.Keys);
        Assert.Equal(200, request.StatusCode);
        Assert.Equal(2, request.Results);
        Assert.Null(request.Error);
        Assert.EndsWith(IndexerRequestTrace.Redact(sent.Query), request.Url, StringComparison.Ordinal);
        Assert.Contains("apikey=•••", request.Url, StringComparison.Ordinal);
        Assert.StartsWith("http://•••@indexer.invalid/1", request.Url, StringComparison.Ordinal);
        Assert.Contains("tmdbid=603", request.Url, StringComparison.Ordinal);

        // What became of the answers: both releases are the film and parse.
        Assert.Equal(2, report.Releases);
        Assert.Equal(2, report.Parsed);
        Assert.Equal(2, report.Matched);

        var skipped = Assert.Single(result.Queries.Single(q => q.IndexerId == down.Id).Requests);
        Assert.True(skipped.Skipped);
        Assert.Equal(IndexerFailureKind.Disabled, skipped.ErrorKind);
        Assert.Null(skipped.Url);

        // Out of its pause, the failure is reported with its status and kind.
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IndexerGateway>().Forget(down.Id);
        }

        var failed = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&includeQueries=true", Json))!;
        var error = failed.Queries!.Single(q => q.IndexerId == down.Id).Requests.Last();
        Assert.False(error.Skipped);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(IndexerFailureKind.ServerError, error.ErrorKind);
        Assert.Null(error.Results);
        Assert.Contains("apikey=•••", error.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_season_search_reports_the_id_request_then_the_title_fallback()
    {
        // The ids find nothing, so the titles are asked: Sonarr's tiers.
        var torznab = new StubHttpHandler(request =>
        {
            var query = request.RequestUri!.Query;
            return StubHttpHandler.Xml(query.Contains("t=caps", StringComparison.Ordinal) ? TorznabRecorded.Caps
                : query.Contains("tvdbid=", StringComparison.Ordinal) ? TorznabRecorded.EmptyFeed
                : TorznabRecorded.Feed);
        });
        using var app = new TorznabFactory(torznab, series: true);
        var client = await ClientAsync(app);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var indexer = await AddTorznabAsync(client, "Demo Torznab", "http://indexer.invalid/2");

        var response = await client.GetAsync($"/api/v1/series/{series.Id}/search?season=1&includeQueries=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ApiKey, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var result = (await response.Content.ReadFromJsonAsync<SeriesSearchResponse>(Json))!;

        var report = Assert.Single(result.Queries!);
        Assert.Equal(indexer.Id, report.IndexerId);
        var steps = report.Requests.Select(r => r.Step).ToList();
        Assert.Equal("Capabilities", steps[0]);
        Assert.Equal("caps", report.Requests[0].Function);

        var byIds = report.Requests.Single(r => r.Step == "IDs");
        Assert.Equal("tvsearch", byIds.Function);
        Assert.Equal(SeriesSearchTests.BreakingBad.ToString(System.Globalization.CultureInfo.InvariantCulture), byIds.Parameters["tvdbid"]);
        Assert.Equal("1", byIds.Parameters["season"]);
        Assert.False(byIds.Parameters.ContainsKey("ep"));
        Assert.Equal(0, byIds.Results);

        var fallback = report.Requests.Where(r => r.Step == "Fallback: Title").ToList();
        Assert.NotEmpty(fallback);
        Assert.Equal("Breaking Bad", fallback[0].Parameters["q"]);
        Assert.Equal("1", fallback[0].Parameters["season"]);
        Assert.Equal(2, fallback[0].Results);
        Assert.True(steps.IndexOf("IDs") < steps.IndexOf("Fallback: Title"));

        // Every request reported is one the stub received, in the same order, redacted.
        Assert.Equal(torznab.Requests.Select(u => IndexerRequestTrace.Redact(u.OriginalString)), report.Requests.Select(r => r.Url));
        Assert.All(report.Requests, r => Assert.Contains("apikey=•••", r.Url, StringComparison.Ordinal));

        // The feed holds film releases: they parse as nothing of this show.
        Assert.Equal(0, report.Matched);
    }

    [Theory]
    [InlineData("http://x/api?t=movie&apikey=k3y&cat=2000", "http://x/api?t=movie&apikey=•••&cat=2000")]
    [InlineData("https://user:pw@x/1/api?t=caps&APIKEY=k3y", "https://•••@x/1/api?t=caps&APIKEY=•••")]
    [InlineData("http://x/rss?passkey=abc&token=def&q=key", "http://x/rss?passkey=•••&token=•••&q=key")]
    [InlineData("http://x/api?t=search&q=monkey&auth=1", "http://x/api?t=search&q=monkey&auth=•••")]
    public void Urls_are_redacted_on_the_server(string url, string expected) => Assert.Equal(expected, IndexerRequestTrace.Redact(url));

    private static async Task<IndexerResponse> AddTorznabAsync(HttpClient client, string name, string url)
    {
        var response = await client.PostAsJsonAsync("/api/v1/indexers",
            new IndexerRequest(name, IndexerImplementation.Torznab, BaseUrl: url, ApiKey: ApiKey, RateLimitSeconds: 0), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
    }

    private static async Task<Bigarr.Api.Features.Movies.MovieResponse> AddMovieAsync(HttpClient client, int tmdbId, int profileId)
    {
        var dir = Directory.CreateTempSubdirectory("bigarr-queries-").FullName;
        var root = await client.PostAsJsonAsync("/api/v1/rootfolders", new Bigarr.Api.Features.Library.AddRootFolderRequest(dir));
        Assert.Equal(HttpStatusCode.Created, root.StatusCode);
        var rootId = (await root.Content.ReadFromJsonAsync<Bigarr.Api.Features.Library.RootFolderResponse>(Json))!.Id;
        var response = await client.PostAsJsonAsync("/api/v1/movies", new Bigarr.Api.Features.Movies.AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Bigarr.Api.Features.Movies.MovieResponse>(Json))!;
    }

    /// <summary>The real app with its Torznab HTTP client answered by the stub; the series fixture clock when asked.</summary>
    private sealed class TorznabFactory(StubHttpHandler torznab, bool series)
        : BigarrApiFactory(null, null, deleteOnDispose: true, time: series ? new FixedTimeProvider(SeriesEndpointsTests.Now) : null)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddHttpClient(TorznabClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => torznab));
        }
    }
}
