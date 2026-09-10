using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Tests.Search;

/// <summary>The RSS loop on the mock feed: fetch, dedupe, match to the library, judge with the automatic rules, attribute.</summary>
public sealed class RssSyncTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task A_pass_judges_new_releases_once_and_reports_per_indexer()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var matrix = await AddMovieAsync(client, 603, profileId, rootId);
        var inception = await AddMovieAsync(client, 27205, profileId, rootId);

        var before = (await client.GetFromJsonAsync<RssStatusResponse>("/api/v1/rss", Json))!;
        Assert.True(before.Enabled);
        Assert.Equal(15, before.IntervalMinutes);
        Assert.False(before.Running);

        var noIndexer = (await (await client.PostAsync("/api/v1/rss/sync", null)).Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
        Assert.Equal(0, noIndexer.Indexers);

        var mock = await AddIndexerAsync(client, new IndexerRequest("Mock feed", IndexerImplementation.Mock, RateLimitSeconds: 0));
        var noRss = await AddIndexerAsync(client, new IndexerRequest("Mock no rss", IndexerImplementation.Mock, EnableRss: false, RateLimitSeconds: 0));

        try
        {
            var first = await SyncAsync(client);
            Assert.Equal(1, first.Indexers); // the RSS-disabled one is skipped
            Assert.Equal(15, first.Fetched); // 5 films × 3 releases in the mock feed
            Assert.Equal(15, first.New);
            Assert.Equal(6, first.Matched); // The Matrix and Inception
            Assert.Equal(6, first.WouldGrab);
            Assert.Empty(first.IndexerErrors);

            var matched = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=true", Json))!;
            Assert.Equal(6, matched.Count);
            Assert.All(matched, r =>
            {
                Assert.NotNull(r.Movie);
                Assert.Null(r.UnmatchedReason);
                Assert.Equal("any", r.AttributedVersionName);
                var verdict = Assert.Single(r.Versions);
                Assert.True(verdict.Approved);
                Assert.Equal(r.AttributedVersionId, verdict.VersionId);
            });
            Assert.Contains(matched, r => r.Movie!.Id == matrix.Id && r.Title.Contains("Matrix"));
            Assert.Contains(matched, r => r.Movie!.Id == inception.Id && r.Quality == "WEBDL-2160p");

            var unmatched = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=false", Json))!;
            Assert.Equal(9, unmatched.Count);
            Assert.All(unmatched, r =>
            {
                Assert.Null(r.Movie);
                Assert.Empty(r.Versions);
                Assert.Null(r.AttributedVersionId);
                Assert.Matches("^TMDB \\d+ is not in the library$", r.UnmatchedReason);
            });

            var all = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!;
            Assert.Equal(15, all.Count);
            Assert.True(all[0].PublishDate >= all[^1].PublishDate, "newest first");
            Assert.All(all, r => Assert.Equal(mock.Id, r.IndexerId));

            // Same feed again: nothing new, nothing re-judged.
            var second = await SyncAsync(client);
            Assert.Equal(15, second.Fetched);
            Assert.Equal(0, second.New);
            Assert.Equal(0, second.Matched);
            Assert.Equal(15, (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!.Count);

            var status = (await client.GetFromJsonAsync<RssStatusResponse>("/api/v1/rss", Json))!;
            Assert.NotNull(status.LastRunAt);
            Assert.Equal(15, status.Stored);
            Assert.Equal(6, status.Matched);
            Assert.Equal(6, status.WouldGrab);
            var indexerStatus = Assert.Single(status.Indexers);
            Assert.Equal(mock.Id, indexerStatus.IndexerId);
            Assert.Equal(15, indexerStatus.LastFetched);
            Assert.Equal(0, indexerStatus.LastNew);
            Assert.Null(indexerStatus.LastError);

            // Unmonitor The Matrix, add a second feed (new guids): its Matrix releases are refused, Inception still goes.
            var version = matrix.Versions.Single();
            var unmonitor = await client.PutAsJsonAsync($"/api/v1/movies/{matrix.Id}/versions/{version.Id}", new UpdateVersionRequest(Monitored: false), Json);
            Assert.Equal(HttpStatusCode.OK, unmonitor.StatusCode);
            var second_feed = await AddIndexerAsync(client, new IndexerRequest("Mock feed two", IndexerImplementation.Mock, RateLimitSeconds: 0));
            try
            {
                var third = await SyncAsync(client);
                Assert.Equal(2, third.Indexers);
                Assert.Equal(15, third.New);
                Assert.Equal(6, third.Matched);
                Assert.Equal(3, third.WouldGrab);

                var refused = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases?matched=true", Json))!
                    .Where(r => r.IndexerId == second_feed.Id && r.Movie!.Id == matrix.Id).ToList();
                Assert.Equal(3, refused.Count);
                Assert.All(refused, r =>
                {
                    Assert.Null(r.AttributedVersionId);
                    Assert.Equal("Version is not monitored", Assert.Single(Assert.Single(r.Versions).Rejections).Reason);
                });
            }
            finally
            {
                await client.DeleteAsync($"/api/v1/indexers/{second_feed.Id}");
            }

            // Deleting an indexer drops its line from the status.
            Assert.DoesNotContain((await client.GetFromJsonAsync<RssStatusResponse>("/api/v1/rss", Json))!.Indexers, i => i.IndexerId == second_feed.Id);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{mock.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{noRss.Id}");
            await client.DeleteAsync($"/api/v1/movies/{matrix.Id}");
            await client.DeleteAsync($"/api/v1/movies/{inception.Id}");
        }
    }

    [Fact]
    public async Task Indexer_failures_are_reported_in_the_pass_and_on_the_indexer_line()
    {
        var client = await ClientAsync();
        var dead = await AddIndexerAsync(client, new IndexerRequest("Dead feed", IndexerImplementation.Torznab, BaseUrl: "http://127.0.0.1:1", ApiKey: "x", RateLimitSeconds: 0));
        try
        {
            var result = await SyncAsync(client);
            var error = Assert.Single(result.IndexerErrors, e => e.IndexerId == dead.Id);
            Assert.Equal(IndexerFailureKind.Unreachable, error.Kind);

            var status = (await client.GetFromJsonAsync<RssStatusResponse>("/api/v1/rss", Json))!;
            var line = status.Indexers.Single(i => i.IndexerId == dead.Id);
            Assert.Contains("Could not reach", line.LastError);
            Assert.Equal(0, line.LastFetched);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{dead.Id}");
        }
    }

    private static async Task<RssSyncResponse> SyncAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/rss/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
    }

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<(int ProfileId, int RootId)> SetupAsync(HttpClient client)
    {
        var profiles = await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        var any = profiles!.Single(p => p.Name == QualitySeeder.AnyProfileName);

        var roots = await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json);
        if (roots!.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-rss-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (any.Id, roots[0].Id);
    }

    private static async Task<MovieResponse> AddMovieAsync(HttpClient client, int tmdbId, int profileId, int rootId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }

    private static async Task<IndexerResponse> AddIndexerAsync(HttpClient client, IndexerRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/indexers", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
    }
}
