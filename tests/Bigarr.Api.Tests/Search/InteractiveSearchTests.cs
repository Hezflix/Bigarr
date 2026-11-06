using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Indexers;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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
            Assert.Equal(version.Id, best.SuggestedVersionId);
            Assert.Null(result.Query);

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

            // Not the film, still judged by the version: grabbing it by hand stays the user's call.
            var sequel = result.Releases.Single(r => r.Title.Contains("2.Reloaded"));
            Assert.False(sequel.MatchesMovie);
            Assert.Equal(version.Id, Assert.Single(sequel.Versions).VersionId);
            Assert.Equal(version.Id, sequel.SuggestedVersionId);
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

    [Fact]
    public async Task The_search_is_for_the_film_and_each_release_suggests_the_version_it_fits()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var uhdProfileId = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!.Single(p => p.Name == QualitySeeder.UhdProfileName).Id;
        var movie = await AddMovieAsync(client, 27205, profileId, rootId); // Inception
        var hd = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock targets", minimumSeeders: 0);

        try
        {
            var uhd = await AddVersionAsync(client, movie.Id, new AddVersionRequest("4k", VersionMode.Managed, uhdProfileId));
            var cut = await AddVersionAsync(client, movie.Id,
                new AddVersionRequest("directors-cut", VersionMode.OneShot, profileId, Constraints: new ConstraintsDto("director'?s cut", null, null, null)));

            var result = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            Assert.All(result.Releases, r => Assert.Equal([hd.Id, uhd.Id, cut.Id], r.Versions.Select(v => v.VersionId)));

            // Accepted by one version only: that one.
            var sparks = result.Releases.Single(r => r.Title == "Inception.2010.1080p.BluRay.x264-SPARKS");
            Assert.Equal([true, false, false], sparks.Versions.Select(v => v.Approved));
            Assert.Equal(hd.Id, sparks.SuggestedVersionId);

            var terminal = result.Releases.Single(r => r.Title.Contains("2160p.UHD.BluRay.x265"));
            Assert.Equal([false, true, false], terminal.Versions.Select(v => v.Approved));
            Assert.Equal(uhd.Id, terminal.SuggestedVersionId);

            // Accepted by several: the RSS attribution rule, the most specific constraints first.
            var directorsCut = result.Releases.Single(r => r.Title.Contains("Directors.Cut"));
            Assert.True(directorsCut.Versions.Single(v => v.VersionId == hd.Id).Approved);
            Assert.True(directorsCut.Versions.Single(v => v.VersionId == cut.Id).Approved);
            Assert.Equal(cut.Id, directorsCut.SuggestedVersionId);

            // Accepted by none: the closest, here a tie on one permanent rejection settled by the default version.
            var cam = result.Releases.Single(r => r.Title.Contains("HDTS"));
            Assert.All(cam.Versions, v => Assert.False(v.Approved));
            Assert.Equal(hd.Id, cam.SuggestedVersionId);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task A_free_text_search_is_judged_against_the_films_versions_and_a_mismatch_can_be_grabbed_by_hand()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddMovieAsync(client, 603, profileId, rootId); // The Matrix
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock text", minimumSeeders: 0);
        var downloads = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Mock text client", DownloadClientImplementation.Mock, Category: "mock-text"), Json);
        Assert.Equal(HttpStatusCode.Created, downloads.StatusCode);
        var downloadClient = (await downloads.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;

        try
        {
            // The mock names its releases after whatever it is asked: here the user's text, not the film's title.
            var result = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&query={Uri.EscapeDataString("  Matrice 1999 ")}", Json))!;
            Assert.Equal("Matrice 1999", result.Query);
            Assert.Equal(17, result.Releases.Count);
            Assert.All(result.Releases, r => Assert.StartsWith("Matrice.1999", r.Title));
            Assert.All(result.Releases, r => Assert.False(r.MatchesMovie));

            var sparks = result.Releases.Single(r => r.Title == "Matrice.1999.1080p.BluRay.x264-SPARKS");
            Assert.Equal("Title 'Matrice' does not match The Matrix", Assert.Single(sparks.Rejections).Reason);
            Assert.True(Assert.Single(sparks.Versions).Approved);
            Assert.Equal(version.Id, sparks.SuggestedVersionId);

            // An interactive grab is the user's decision: a release that is not the film by its title goes through.
            var grabbed = await client.PostAsJsonAsync("/api/v1/grab", new GrabRequest(
                version.Id, sparks.IndexerId, sparks.Guid, sparks.Title, sparks.DownloadUrl, sparks.MagnetUrl, sparks.InfoHash,
                sparks.Size, sparks.Seeders, sparks.Flags, sparks.Protocol, sparks.Indexer), Json);
            Assert.Equal(HttpStatusCode.Created, grabbed.StatusCode);
            Assert.Equal(GrabState.Grabbed, (await grabbed.Content.ReadFromJsonAsync<GrabResponse>(Json))!.State);

            // A blank text is no text: the film is searched by its ids and titles again.
            var blank = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&query=%20%20", Json))!;
            Assert.Null(blank.Query);
            Assert.Contains(blank.Releases, r => r.Title == "The.Matrix.1999.1080p.BluRay.x264-SPARKS" && r.MatchesMovie);

            var tooLong = await client.GetAsync($"/api/v1/search?movieId={movie.Id}&query={new string('a', SearchEndpoints.MaxQueryLength + 1)}");
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains("query", await tooLong.Content.ReadAsStringAsync());
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task Torznab_indexers_get_the_films_ids_or_the_users_text_never_both()
    {
        var torznab = new StubHttpHandler(request =>
            StubHttpHandler.Xml(request.RequestUri!.Query.Contains("t=caps", StringComparison.Ordinal) ? TorznabRecorded.Caps : TorznabRecorded.Feed));
        using var app = new TorznabFactory(torznab);
        var client = await LoginAsync(app);
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddMovieAsync(client, 603, profileId, rootId); // The Matrix
        var created = await client.PostAsJsonAsync("/api/v1/indexers",
            new IndexerRequest("Torznab", IndexerImplementation.Torznab, BaseUrl: "http://indexer.invalid/1", ApiKey: "k3y", RateLimitSeconds: 0), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var film = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
        Assert.Equal(2, film.Releases.Count);
        var byIds = HttpUtility.ParseQueryString(torznab.Requests.Last().Query);
        Assert.Equal("movie", byIds["t"]);
        Assert.Equal("603", byIds["tmdbid"]);
        Assert.Null(byIds["q"]);

        var text = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}&query={Uri.EscapeDataString("Matrix: Reloaded!")}", Json))!;
        Assert.Equal("Matrix: Reloaded!", text.Query);
        var byText = HttpUtility.ParseQueryString(torznab.Requests.Last().Query);
        Assert.Equal("movie", byText["t"]);
        Assert.Equal("Matrix Reloaded", byText["q"]);
        Assert.Null(byText["tmdbid"]);
        Assert.Null(byText["imdbid"]);
        Assert.Null(byText["year"]);

        // Whatever the text, the releases are judged against the film: these two carry its ids.
        Assert.All(text.Releases, r => Assert.True(r.MatchesMovie));
        Assert.All(text.Releases, r => Assert.Equal(movie.Versions.Single().Id, Assert.Single(r.Versions).VersionId));
    }

    private Task<HttpClient> ClientAsync() => LoginAsync(factory);

    private static async Task<HttpClient> LoginAsync(BigarrApiFactory app)
    {
        var client = app.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<VersionResponse> AddVersionAsync(HttpClient client, int movieId, AddVersionRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/movies/{movieId}/versions", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
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

    /// <summary>The real app with its Torznab HTTP client answered by a stub that records every query string.</summary>
    private sealed class TorznabFactory(StubHttpHandler torznab) : BigarrApiFactory(null, null)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddHttpClient(TorznabClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => torznab));
        }
    }
}
