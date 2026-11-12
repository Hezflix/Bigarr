using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Automation;

/// <summary>M6, mode auto: the best approved release is grabbed without anyone picking it.</summary>
public sealed class AutomaticSearchTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task A_user_search_grabs_the_best_release_and_respects_the_active_download()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-auto-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var version = movie.Versions.Single();

        try
        {
            // Nothing to query yet.
            var none = await PostSearchAsync(client, movie.Id);
            var noIndexer = Assert.Single(none);
            Assert.Equal(AutomaticSearchOutcome.NoIndexer, noIndexer.Outcome);

            var indexer = await AddMockIndexerAsync(client, "Mock auto");
            var downloadClient = await AddMockClientAsync(client, "Mock auto qbt");
            try
            {
                var results = await PostSearchAsync(client, movie.Id);
                var result = Assert.Single(results);
                Assert.Equal(AutomaticSearchOutcome.Grabbed, result.Outcome);
                Assert.Equal(version.Id, result.VersionId);
                Assert.Contains("REMUX", result.ReleaseTitle, StringComparison.Ordinal); // the top of the ranking for the HD profile
                Assert.NotNull(result.GrabId);
                Assert.True(result.Approved > 1);
                Assert.True(result.Releases >= result.Approved);

                var history = await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed);
                var grabbed = Assert.Single(history);
                Assert.Equal("search:user", grabbed.Data["trigger"]);
                Assert.Equal(result.ReleaseTitle, grabbed.SourceTitle);

                var refreshed = await GetMovieAsync(client, movie.Id);
                Assert.Equal(VersionStatus.Downloading, refreshed.Versions.Single().Status);

                // Searching again while the download is active grabs nothing more.
                var again = Assert.Single(await PostSearchAsync(client, movie.Id));
                Assert.Equal(AutomaticSearchOutcome.Skipped, again.Outcome);
                Assert.Contains("already has an active download", again.Detail, StringComparison.Ordinal);

                Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/movies/999999/search", null)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/movies/{movie.Id}/search?versionId=999999", null)).StatusCode);
            }
            finally
            {
                await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
                await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            }
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task Search_on_add_runs_in_the_background_for_films_and_versions()
    {
        var client = await ClientAsync(factory);
        var (hd, uhd) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-auto-{Guid.NewGuid():N}"));
        var indexer = await AddMockIndexerAsync(client, "Mock on add");
        var downloadClient = await AddMockClientAsync(client, "Mock on add qbt");
        MovieResponse? movie = null;

        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(27205, hd, root.Id, SearchOnAdd: true), Json);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            movie = (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;

            var grabbed = await WaitUntilAsync(async () => (await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed)).FirstOrDefault(), "the search-on-add grab");
            Assert.Equal("search:added", grabbed.Data["trigger"]);
            Assert.Equal(movie.Versions.Single().Id, grabbed.VersionId);
            Assert.Contains("1080p", grabbed.SourceTitle, StringComparison.Ordinal);

            // A 4K version added with search on add gets its own 2160p release.
            var uhdVersion = await AddVersionAsync(client, movie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhd, SearchOnAdd: true));
            var grabbed4K = await WaitUntilAsync(async () => (await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed)).FirstOrDefault(h => h.VersionId == uhdVersion.Id), "the 4K search-on-add grab");
            Assert.Contains("2160p", grabbed4K.SourceTitle, StringComparison.Ordinal);

            var refreshed = await GetMovieAsync(client, movie.Id);
            Assert.All(refreshed.Versions, v => Assert.Equal(VersionStatus.Downloading, v.Status));
        }
        finally
        {
            if (movie is not null)
            {
                await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            }

            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            TryDelete(root.Path);
        }
    }

    [Fact]
    public async Task The_wanted_list_feeds_the_cutoff_unmet_job_with_a_backoff()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-auto-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 157336, hd, root.Id); // Interstellar
        var version = movie.Versions.Single();
        // A seeder floor nothing reaches: every release is rejected, so the search comes back empty.
        var picky = await client.PostAsJsonAsync("/api/v1/indexers", new Bigarr.Api.Features.Indexers.IndexerRequest("Mock picky", Bigarr.Domain.Indexers.IndexerImplementation.Mock, MinimumSeeders: 5000, RateLimitSeconds: 0), Json);
        var indexer = (await picky.Content.ReadFromJsonAsync<Bigarr.Api.Features.Indexers.IndexerResponse>(Json))!;
        var downloadClient = await AddMockClientAsync(client, "Mock wanted qbt");

        try
        {
            var wanted = (await client.GetFromJsonAsync<WantedPageResponse>("/api/v1/wanted?filter=missing", Json))!;
            var item = Assert.Single(wanted.Items, i => i.VersionId == version.Id);
            Assert.Equal(WantedFilter.Missing, item.Kind);
            Assert.Null(item.LastSearchAt);
            Assert.Null(item.NextSearchAt);
            Assert.Equal(0, item.SearchAttempts);
            Assert.Equal("Bluray-1080p", item.CutoffQuality);

            var run = await client.PostAsync($"/api/v1/system/tasks/{CutoffUnmetSearchJob.JobName}/run", null);
            Assert.Equal(HttpStatusCode.OK, run.StatusCode);
            var outcome = (await run.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
            Assert.True(outcome.Success);
            Assert.Contains("0 grabbed", outcome.Summary, StringComparison.Ordinal);

            // Empty search: the backoff starts, the version stays wanted but is no longer due.
            wanted = (await client.GetFromJsonAsync<WantedPageResponse>("/api/v1/wanted?filter=missing", Json))!;
            item = Assert.Single(wanted.Items, i => i.VersionId == version.Id);
            Assert.NotNull(item.LastSearchAt);
            Assert.Equal(1, item.SearchAttempts);
            Assert.Equal(item.LastSearchAt!.Value.AddHours(1), item.NextSearchAt);

            var second = (await (await client.PostAsync($"/api/v1/system/tasks/{CutoffUnmetSearchJob.JobName}/run", null)).Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
            Assert.Equal("Nothing due", second.Summary);

            // The user does not wait: the wanted page queues a search that ignores the backoff, and an easier indexer lets it grab.
            var easy = await AddMockIndexerAsync(client, "Mock easy");
            try
            {
                var queued = await client.PostAsync("/api/v1/wanted/search?filter=missing", null);
                Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
                Assert.Equal(1, (await queued.Content.ReadFromJsonAsync<WantedSearchResponse>(Json))!.Queued);

                var grabbed = await WaitUntilAsync(async () => (await HistoryAsync(client, movie.Id, HistoryEventType.Grabbed)).FirstOrDefault(), "the wanted search grab");
                Assert.Equal("search:user", grabbed.Data["trigger"]);

                wanted = (await client.GetFromJsonAsync<WantedPageResponse>("/api/v1/wanted", Json))!;
                Assert.DoesNotContain(wanted.Items, i => i.VersionId == version.Id); // downloading is not wanted
            }
            finally
            {
                await client.DeleteAsync($"/api/v1/indexers/{easy.Id}");
            }
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }

    private static async Task<List<AutomaticSearchResult>> PostSearchAsync(HttpClient client, int movieId)
    {
        var response = await client.PostAsync($"/api/v1/movies/{movieId}/search", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<AutomaticSearchResult>>(Json))!;
    }
}
