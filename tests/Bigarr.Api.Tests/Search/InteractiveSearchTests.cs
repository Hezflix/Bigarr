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
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Search;

/// <summary>The whole M2 loop against the mock indexer: query, parse, match, judge, rank.</summary>
public sealed class InteractiveSearchTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Searching_a_movie_returns_ranked_releases_with_their_rejections()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);

        var movie = await AddMovieAsync(client, 603, profileId, rootId);
        var version = movie.Versions.Single();

        var missing = await client.GetAsync($"/api/v1/search?movieId={movie.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("No indexer available", await missing.Content.ReadAsStringAsync());

        var indexer = await AddMockIndexerAsync(client, "Mock", minimumSeeders: 1);
        try
        {
            var response = await client.GetAsync($"/api/v1/search?movieId={movie.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"flags\":\"internal\"", raw);

            var result = JsonSerializer.Deserialize<SearchResponse>(raw, Json)!;
            Assert.Equal(movie.Id, result.MovieId);
            Assert.Equal(1, result.IndexersQueried);
            Assert.Empty(result.IndexerErrors);
            Assert.Equal(17, result.Releases.Count);

            // Best first: matched, approved, highest quality the profile wants.
            var best = result.Releases[0];
            Assert.True(best.MatchesMovie);
            Assert.Contains("1080p.BluRay.REMUX", best.Title);
            Assert.Equal("Remux-1080p", best.Quality);
            Assert.Equal(1080, best.Resolution);
            Assert.Equal("EPSiLON", best.ReleaseGroup);
            Assert.Equal(1999, best.ParsedYear);
            Assert.Equal("The Matrix", best.ParsedTitle);
            Assert.Equal("Mock", best.Indexer);
            Assert.Equal(indexer.Id, best.IndexerId);
            Assert.True(best.Size > 25L * 1024 * 1024 * 1024);
            Assert.NotNull(best.InfoHash);
            var verdict = Assert.Single(best.Versions);
            Assert.Equal(version.Id, verdict.VersionId);
            Assert.Equal(version.Name, verdict.VersionName);
            Assert.True(verdict.Approved);

            // Approved releases come before rejected ones, rejected-but-matched before mismatches.
            var order = result.Releases.Select(r => (r.MatchesMovie, Approved: r.Versions.Any(v => v.Approved))).ToList();
            var firstRejected = order.FindIndex(o => o.MatchesMovie && !o.Approved);
            var firstMismatch = order.FindIndex(o => !o.MatchesMovie);
            Assert.True(order.Take(firstRejected).All(o => o.Approved));
            Assert.True(firstMismatch > firstRejected);
            Assert.True(order.Skip(firstMismatch).All(o => !o.MatchesMovie));

            // 4K is out of the seeded 1080p profile: matched and judged, refused by name.
            var uhd = result.Releases.Single(r => r.Title.Contains("2160p.UHD.BluRay.REMUX"));
            Assert.True(uhd.MatchesMovie);
            Assert.Contains(Assert.Single(uhd.Versions).Rejections,
                r => r.Reason == $"Remux-2160p is not wanted in profile {QualitySeeder.HdProfileName}" && !r.Temporary);

            var repack = result.Releases.Single(r => r.Title.Contains("REPACK"));
            Assert.True(repack.IsRepack);
            Assert.Equal(2, repack.RevisionVersion);

            var noSeed = result.Releases.Single(r => r.Title.EndsWith("-NOSEED"));
            var noSeedVerdict = Assert.Single(noSeed.Versions);
            Assert.False(noSeedVerdict.Approved);
            var rejection = Assert.Single(noSeedVerdict.Rejections);
            Assert.Equal("Not enough seeders: 0 (minimum 1)", rejection.Reason);
            Assert.True(rejection.Temporary);

            var bloated = result.Releases.Single(r => r.Title.Contains("-BLOAT"));
            Assert.Contains(Assert.Single(bloated.Versions).Rejections, r => r.Reason.Contains("larger than the maximum") && !r.Temporary);

            var noQuality = result.Releases.Single(r => r.Title == "The.Matrix.1999-GRP");
            Assert.True(noQuality.MatchesMovie);
            Assert.Equal("Unknown", noQuality.Quality);
            Assert.Contains(Assert.Single(noQuality.Versions).Rejections, r => r.Reason.Contains("could not be determined"));

            var sequel = result.Releases.Single(r => r.Title.Contains("2.Reloaded"));
            Assert.False(sequel.MatchesMovie);
            Assert.Empty(sequel.Versions);
            Assert.Equal("Title 'The Matrix 2 Reloaded' does not match The Matrix", Assert.Single(sequel.Rejections).Reason);

            var wrongYear = result.Releases.Single(r => r.Title.Contains(".2006."));
            Assert.False(wrongYear.MatchesMovie);
            Assert.Equal("Year 2006 does not match 1999", Assert.Single(wrongYear.Rejections).Reason);

            var french = result.Releases.Single(r => r.Title.Contains("FRENCH"));
            Assert.Contains("French", french.Languages);

            var directorsCut = result.Releases.Single(r => r.Title.Contains("Directors.Cut"));
            Assert.Equal("Directors Cut", directorsCut.Edition);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Version_constraints_and_existing_files_shape_the_verdicts()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddMovieAsync(client, 27205, profileId, rootId); // Inception
        var indexer = await AddMockIndexerAsync(client, "Mock constraints", minimumSeeders: 0);

        try
        {
            var addVersion = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions",
                new AddVersionRequest("directors-cut", VersionMode.OneShot, profileId, Constraints: new ConstraintsDto("director'?s cut", null, null, null)), Json);
            Assert.Equal(HttpStatusCode.Created, addVersion.StatusCode);
            var oneShot = (await addVersion.Content.ReadFromJsonAsync<VersionResponse>(Json))!;

            // Every version at once: one verdict per version on each matched release.
            var all = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            var sparks = all.Releases.Single(r => r.Title == "Inception.2010.1080p.BluRay.x264-SPARKS");
            Assert.Equal(2, sparks.Versions.Count);
            Assert.True(sparks.Versions.Single(v => v.VersionName == "1080p").Approved);
            var refused = sparks.Versions.Single(v => v.VersionId == oneShot.Id);
            Assert.False(refused.Approved);
            Assert.StartsWith("Edition must match", Assert.Single(refused.Rejections).Reason);

            var cut = all.Releases.Single(r => r.Title.Contains("Directors.Cut"));
            Assert.True(cut.Versions.Single(v => v.VersionId == oneShot.Id).Approved);

            // One version only.
            var only = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&versionId={oneShot.Id}", Json))!;
            Assert.All(only.Releases.Where(r => r.MatchesMovie), r => Assert.Equal(oneShot.Id, Assert.Single(r.Versions).VersionId));
            Assert.True(only.Releases[0].Versions[0].Approved);
            Assert.Contains("Directors.Cut", only.Releases[0].Title);

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/search?movieId={movie.Id}&versionId=999999")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/search?movieId=999999")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/search")).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task Indexers_disabled_for_interactive_search_are_skipped_and_failures_are_reported_per_indexer()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddMovieAsync(client, 157336, profileId, rootId); // Interstellar

        var mock = await AddMockIndexerAsync(client, "Mock live", minimumSeeders: 1);
        var disabled = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest("Mock off", IndexerImplementation.Mock, EnableInteractiveSearch: false), Json);
        var off = (await disabled.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
        var deadResponse = await client.PostAsJsonAsync("/api/v1/indexers",
            new IndexerRequest("Dead Torznab", IndexerImplementation.Torznab, BaseUrl: "http://127.0.0.1:1", ApiKey: "x", RateLimitSeconds: 0), Json);
        var dead = (await deadResponse.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;

        try
        {
            var result = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            Assert.Equal(2, result.IndexersQueried);
            Assert.All(result.Releases, r => Assert.Equal(mock.Id, r.IndexerId));

            var error = Assert.Single(result.IndexerErrors);
            Assert.Equal(dead.Id, error.IndexerId);
            Assert.Equal(IndexerFailureKind.Unreachable, error.Kind);

            // The failure put the dead indexer in backoff, visible on its row.
            var status = (await client.GetFromJsonAsync<IndexerResponse>($"/api/v1/indexers/{dead.Id}", Json))!.Status;
            Assert.False(status.Healthy);
            Assert.Equal(1, status.Failures);
            Assert.NotNull(status.DisabledUntil);

            // A second search does not even try it.
            var again = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            Assert.Equal(IndexerFailureKind.Disabled, Assert.Single(again.IndexerErrors).Kind);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{mock.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{off.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{dead.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
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
        var hd = profiles!.Single(p => p.Name == QualitySeeder.HdProfileName);

        var roots = await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json);
        if (roots!.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-search-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (hd.Id, roots[0].Id);
    }

    private static async Task<MovieResponse> AddMovieAsync(HttpClient client, int tmdbId, int profileId, int rootId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }

    private static async Task<IndexerResponse> AddMockIndexerAsync(HttpClient client, string name, int minimumSeeders)
    {
        var response = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest(name, IndexerImplementation.Mock, MinimumSeeders: minimumSeeders, RateLimitSeconds: 0), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
    }
}
