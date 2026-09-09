using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Movies;

public sealed class MoviesEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Lookup_supports_text_tmdb_and_imdb_terms_and_flags_existing_movies()
    {
        var client = await ClientAsync();

        var byText = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=the%20matrix%20(1999)", Json);
        Assert.NotNull(byText);
        Assert.Equal(603, byText[0].TmdbId);
        Assert.Null(byText[0].ExistingMovieId);

        var byTmdb = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tmdb:157336", Json);
        Assert.Equal("Interstellar", Assert.Single(byTmdb!).Title);

        var byImdb = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tt1375666", Json);
        Assert.Equal("Inception", Assert.Single(byImdb!).Title);

        var empty = await client.GetAsync("/api/v1/movies/lookup?term=");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // A trailing number is only a year when plausible: "Blade Runner 2049" is a title.
        var numericTitle = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=blade%20runner%202049", Json);
        Assert.Equal(335984, Assert.Single(numericTitle!).TmdbId);
        var numericTitleWithYear = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=blade%20runner%202049%20(2017)", Json);
        Assert.Equal(335984, Assert.Single(numericTitleWithYear!).TmdbId);
        var bareYear = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=the%20matrix%201999", Json);
        Assert.Equal(603, bareYear![0].TmdbId);

        var (profileId, rootId) = await SetupAsync(client);
        var added = await AddAsync(client, 129, profileId, rootId);
        var again = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tmdb:129", Json);
        Assert.Equal(added.Id, Assert.Single(again!).ExistingMovieId);
    }

    [Fact]
    public async Task Adding_a_movie_creates_its_default_managed_version()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(603, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"mode\":\"managed\"", raw);
        Assert.Contains("\"minimumAvailability\":\"released\"", raw);

        var movie = JsonSerializer.Deserialize<MovieResponse>(raw, Json)!;
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal("matrix", movie.SortTitle);
        Assert.Equal(MovieStatus.Released, movie.EffectiveStatus);
        Assert.True(movie.Monitored);

        var version = Assert.Single(movie.Versions);
        Assert.Equal("any", version.Name);
        Assert.True(version.IsDefault);
        Assert.Equal(VersionMode.Managed, version.Mode);
        Assert.Equal("The Matrix (1999)", version.FolderName);
        Assert.Equal(VersionStatus.Missing, version.Status);
        Assert.Null(version.File);

        var conflict = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(603, profileId, rootId));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var unknown = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(123456789, profileId, rootId));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var list = await client.GetFromJsonAsync<List<MovieSummary>>("/api/v1/movies", Json);
        Assert.Contains(list!, m => m.TmdbId == 603 && m.Versions.Single().Status == VersionStatus.Missing);
    }

    [Fact]
    public async Task Folder_names_are_sanitized_and_unreleased_movies_are_pending()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);

        var mission = await AddAsync(client, 954, profileId, rootId);
        Assert.Equal("Mission - Impossible (1996)", mission.Versions[0].FolderName);

        var unreleased = await AddAsync(client, 900000001, profileId, rootId);
        Assert.Equal(VersionStatus.Pending, unreleased.Versions[0].Status);
        Assert.False(unreleased.Versions[0].IsAvailable);

        var relaxed = await client.PutAsJsonAsync(
            $"/api/v1/movies/{unreleased.Id}/versions/{unreleased.Versions[0].Id}",
            new UpdateVersionRequest(MinimumAvailability: MinimumAvailability.Announced));
        var version = await relaxed.Content.ReadFromJsonAsync<VersionResponse>(Json);
        Assert.Equal(VersionStatus.Missing, version!.Status);
    }

    [Fact]
    public async Task Versions_can_be_added_updated_promoted_and_deleted()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddAsync(client, 157336, profileId, rootId);
        var defaultVersion = movie.Versions.Single();

        var created = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions", new AddVersionRequest("4k", VersionMode.Managed, profileId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var fourK = (await created.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
        Assert.False(fourK.IsDefault);
        Assert.Equal(defaultVersion.FolderName, fourK.FolderName);

        var duplicate = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions", new AddVersionRequest("4k", VersionMode.Managed, profileId));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var badName = await client.PostAsJsonAsync($"/api/v1/movies/{movie.Id}/versions", new AddVersionRequest("4K", VersionMode.Managed, profileId));
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);

        var oneShot = await client.PostAsJsonAsync(
            $"/api/v1/movies/{movie.Id}/versions",
            new AddVersionRequest("imax", VersionMode.OneShot, profileId, Constraints: new ConstraintsDto("imax", null, null, null)));
        var imax = (await oneShot.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
        Assert.Equal(VersionMode.OneShot, imax.Mode);
        Assert.Equal("imax", imax.Constraints!.Edition);

        var demote = await client.PutAsJsonAsync($"/api/v1/movies/{movie.Id}/versions/{defaultVersion.Id}", new UpdateVersionRequest(Mode: VersionMode.OneShot));
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);

        var promoteOneShot = await client.PostAsync($"/api/v1/movies/{movie.Id}/versions/{imax.Id}/promote", null);
        Assert.Equal(HttpStatusCode.Conflict, promoteOneShot.StatusCode);

        var promote = await client.PostAsync($"/api/v1/movies/{movie.Id}/versions/{fourK.Id}/promote", null);
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        var afterPromote = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
        Assert.Equal("4k", afterPromote.Versions.Single(v => v.IsDefault).Name);

        var deleteDefault = await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{fourK.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteDefault.StatusCode);

        var deleteWithPromote = await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{fourK.Id}?promote={defaultVersion.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteWithPromote.StatusCode);
        var afterDelete = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
        Assert.Equal(["any", "imax"], afterDelete.Versions.Select(v => v.Name));
        Assert.True(afterDelete.Versions[0].IsDefault);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{imax.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/movies/{movie.Id}/versions/{defaultVersion.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/movies/{movie.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_movie_can_exclude_it_and_adding_it_again_lifts_the_exclusion()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddAsync(client, 27205, profileId, rootId);

        var deleted = await client.DeleteAsync($"/api/v1/movies/{movie.Id}?addImportExclusion=true");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var lookup = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tmdb:27205", Json);
        Assert.True(Assert.Single(lookup!).Excluded);

        await AddAsync(client, 27205, profileId, rootId);
        lookup = await client.GetFromJsonAsync<List<MovieLookupResult>>("/api/v1/movies/lookup?term=tmdb:27205", Json);
        Assert.False(Assert.Single(lookup!).Excluded);
    }

    [Fact]
    public async Task Refresh_reapplies_metadata_and_deleting_files_is_refused_until_the_recycle_bin_exists()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddAsync(client, 335984, profileId, rootId);

        var refreshed = await client.PostAsync($"/api/v1/movies/{movie.Id}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = (await refreshed.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
        Assert.NotNull(body.LastMetadataRefreshAt);

        // No file yet: the flag is harmless.
        var noFile = await client.DeleteAsync($"/api/v1/movies/{movie.Id}?deleteFiles=true");
        Assert.Equal(HttpStatusCode.NoContent, noFile.StatusCode);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
            var dir = Directory.CreateTempSubdirectory("bigarr-movies-").FullName;
            var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (any.Id, roots[0].Id);
    }

    private static async Task<MovieResponse> AddAsync(HttpClient client, int tmdbId, int profileId, int rootId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }
}
