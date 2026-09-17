using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Blocklist;
using Bigarr.Api.Tests.Auth;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Blocklist;

public sealed class BlocklistEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Entries_are_listed_with_their_film_and_removed_one_by_one_or_in_bulk()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Path.Combine(Path.GetTempPath(), $"bigarr-blocklist-{Guid.NewGuid():N}"));
        var movie = await AddMovieAsync(client, 603, hd, root.Id);
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock blocklist");
        var downloadClient = await AddMockClientAsync(client, "Mock blocklist qbt");

        try
        {
            var empty = (await client.GetFromJsonAsync<BlocklistPageResponse>("/api/v1/blocklist", Json))!;
            Assert.Equal(0, empty.Total);

            var search = await SearchAsync(client, movie.Id);
            var approved = search.Releases.Where(r => r.Versions.Any(v => v.Approved)).Take(3).ToList();
            foreach (var release in approved)
            {
                var grab = await GrabAsync(client, release, version.Id);
                Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/queue/{grab.GrabId}?removeFromClient=true&deleteFiles=false&blocklist=true")).StatusCode);
            }

            var page = (await client.GetFromJsonAsync<BlocklistPageResponse>($"/api/v1/blocklist?movieId={movie.Id}&pageSize=2", Json))!;
            Assert.Equal(3, page.Total);
            Assert.Equal(2, page.Items.Count);
            Assert.All(page.Items, e =>
            {
                Assert.Equal(movie.Id, e.MovieId);
                Assert.Equal("The Matrix", e.MovieTitle);
                Assert.Equal(version.Name, e.VersionName);
                Assert.Equal("Removed from the queue by the user", e.Reason);
                Assert.Equal(indexer.Id, e.IndexerId);
                Assert.NotNull(e.Quality);
            });
            var second = (await client.GetFromJsonAsync<BlocklistPageResponse>($"/api/v1/blocklist?movieId={movie.Id}&pageSize=2&page=2", Json))!;
            Assert.Single(second.Items);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/blocklist/{page.Items[0].Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/blocklist/{page.Items[0].Id}")).StatusCode);

            var bulk = await client.PostAsJsonAsync("/api/v1/blocklist/bulk-delete", new BlocklistBulkDeleteRequest([page.Items[1].Id, second.Items[0].Id, 999999]), Json);
            Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
            Assert.Equal(2, await bulk.Content.ReadFromJsonAsync<int>());

            Assert.Equal(0, (await client.GetFromJsonAsync<BlocklistPageResponse>("/api/v1/blocklist", Json))!.Total);

            // Released from the blocklist, the releases are approved again.
            var judged = await SearchAsync(client, movie.Id);
            Assert.All(approved, r => Assert.True(judged.Releases.Single(x => x.Title == r.Title).Versions.Single().Approved));
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            TryDelete(root.Path);
        }
    }
}
