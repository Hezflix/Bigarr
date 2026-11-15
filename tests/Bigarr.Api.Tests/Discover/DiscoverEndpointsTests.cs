using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Discover;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Discover;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Discover;

/// <summary>
/// Discover against recorded TMDB answers and a demo library: availability per version and per season straight from the
/// library, what a user who is not the admin receives (states, nothing of the library), the row settings and the cache.
/// </summary>
public sealed class DiscoverEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    [Fact]
    public async Task Lists_carry_each_titles_availability_per_version_read_from_the_library()
    {
        await using var env = await Env.StartAsync();
        await env.SeedLibraryAsync();

        var home = await env.Admin.GetFromJsonAsync<DiscoverHomeResponse>("/api/v1/discover", Json);
        Assert.True(home!.Configured);
        Assert.True(home.FourK);
        Assert.Equal(DiscoverSlider.Defaults, home.Sliders.Select(s => s.Kind));
        Assert.Equal(["Action", "Animation", "Science Fiction"], home.Sliders.Single(s => s.Kind == DiscoverSliderKind.MovieGenres).Tiles!.Select(t => t.Name));
        Assert.Contains(home.Sliders.Single(s => s.Kind == DiscoverSliderKind.Networks).Tiles!, t => t.Name == "Netflix");
        Assert.Null(home.Sliders.Single(s => s.Kind == DiscoverSliderKind.Trending).Tiles);

        var trending = await env.Admin.GetFromJsonAsync<DiscoverPage>($"/api/v1/discover/sliders/{Id(home, DiscoverSliderKind.Trending)}", Json);
        Assert.Equal(500, trending!.TotalPages); // TMDB serves 500 pages at most
        Assert.Equal(["The Matrix", "Breaking Bad", "Interstellar"], trending.Results.Select(r => r.Title)); // people left out of a row of titles

        var matrix = trending.Results[0];
        Assert.Equal(DiscoverMediaType.Movie, matrix.MediaType);
        Assert.Equal(1999, matrix.Year);
        Assert.Equal("https://image.tmdb.org/t/p/original/p96dm7sCMn4VYAStA6siNz30G1r.jpg", matrix.PosterUrl);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.Available, AvailabilityState.Processing), matrix.Availability);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.PartiallyAvailable, AvailabilityState.Processing), trending.Results[1].Availability);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.NotInLibrary, AvailabilityState.NotInLibrary), trending.Results[2].Availability);

        // An unmonitored version without a file is not in the library as far as Discover goes.
        var popular = await env.Admin.GetFromJsonAsync<DiscoverPage>($"/api/v1/discover/sliders/{Id(home, DiscoverSliderKind.PopularMovies)}?page=2", Json);
        Assert.Contains("page=2", env.Tmdb.Requests.Last(r => r.AbsolutePath == "/3/movie/popular").Query);
        Assert.Equal(AvailabilityState.NotInLibrary, popular!.Results.Single(r => r.TmdbId == 604).Availability!.Standard);
        Assert.Null(popular.Results.Single(r => r.TmdbId == 27205).PosterUrl);
    }

    [Fact]
    public async Task A_series_title_page_has_the_availability_of_each_season_per_version()
    {
        await using var env = await Env.StartAsync();
        await env.SeedLibraryAsync();

        var series = await env.Admin.GetFromJsonAsync<DiscoverSeriesResponse>("/api/v1/discover/series/1396", Json);
        Assert.Equal("Breaking Bad", series!.Title);
        Assert.Equal(81189, series.TvdbId);
        Assert.Equal(45, series.Runtime);
        Assert.Equal(["AMC"], series.Networks.Select(n => n.Name));
        Assert.Equal(["Vince Gilligan"], series.Creators);
        Assert.NotNull(series.LibraryId);

        // Specials come last; season 3 is on TMDB only.
        Assert.Equal([1, 2, 3, 0], series.Seasons.Select(s => s.SeasonNumber));
        var states = series.Seasons.ToDictionary(s => s.SeasonNumber, s => s.Availability);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.Available, AvailabilityState.Processing), states[1]);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.PartiallyAvailable, AvailabilityState.NotInLibrary), states[2]); // the unaired episode does not count
        Assert.Equal(new AvailabilityResponse(AvailabilityState.NotInLibrary, AvailabilityState.NotInLibrary), states[3]);
        Assert.Equal(new AvailabilityResponse(AvailabilityState.NotInLibrary, AvailabilityState.NotInLibrary), states[0]);

        // The missing special does not make the show partial; season 2 does.
        Assert.Equal(new AvailabilityResponse(AvailabilityState.PartiallyAvailable, AvailabilityState.Processing), series.Availability);
        Assert.Equal(AvailabilityState.NotInLibrary, series.Recommendations.Single().Availability!.Standard);
    }

    [Fact]
    public async Task A_series_added_from_tvdb_without_a_tmdb_id_is_found_on_its_title_page()
    {
        await using var env = await Env.StartAsync();
        await env.SeedLibraryAsync(seriesTmdbId: null);

        var series = await env.Admin.GetFromJsonAsync<DiscoverSeriesResponse>("/api/v1/discover/series/1396", Json);
        Assert.Equal(AvailabilityState.PartiallyAvailable, series!.Availability.Standard);
    }

    [Fact]
    public async Task A_film_title_page_has_details_trailer_cast_and_availability()
    {
        await using var env = await Env.StartAsync();
        var movieId = await env.SeedLibraryAsync();

        var movie = await env.Admin.GetFromJsonAsync<DiscoverMovieResponse>("/api/v1/discover/movies/603", Json);
        Assert.Equal("The Matrix", movie!.Title);
        Assert.Equal("R", movie.Certification);
        Assert.Equal(136, movie.Runtime);
        Assert.Equal(["Lilly Wachowski", "Lana Wachowski"], movie.Directors);
        Assert.Equal(["Keanu Reeves", "Laurence Fishburne"], movie.Cast.Select(c => c.Name));
        Assert.Equal("https://www.youtube.com/watch?v=vKQi3bBA1y8", movie.TrailerUrl);
        Assert.Equal(["Village Roadshow Pictures", "Warner Bros. Pictures"], movie.Studios.Select(s => s.Name));
        Assert.Equal(new AvailabilityResponse(AvailabilityState.Available, AvailabilityState.Processing), movie.Availability);
        Assert.Equal(movieId, movie.LibraryId);
        Assert.Equal(AvailabilityState.NotInLibrary, movie.Recommendations.Single(r => r.TmdbId == 604).Availability!.Standard);

        Assert.Equal(HttpStatusCode.NotFound, (await env.Admin.GetAsync("/api/v1/discover/movies/1")).StatusCode);
    }

    [Fact]
    public async Task Someone_who_is_not_the_admin_browses_and_gets_states_only()
    {
        await using var env = await Env.StartAsync();
        await env.SeedLibraryAsync();
        var user = await env.SignInUserAsync();

        var home = await user.GetFromJsonAsync<DiscoverHomeResponse>("/api/v1/discover", Json);
        var trending = await user.GetStringAsync($"/api/v1/discover/sliders/{Id(home!, DiscoverSliderKind.Trending)}");
        var movie = await user.GetStringAsync("/api/v1/discover/movies/603");
        var series = await user.GetStringAsync("/api/v1/discover/series/1396");
        var search = await user.GetStringAsync("/api/v1/discover/search?query=matrix");
        var person = await user.GetStringAsync("/api/v1/discover/people/6384");

        // Nothing of the library beyond the states: no library id, no paths, sizes, releases, profiles, root folders.
        foreach (var body in new[] { trending, movie, series, search, person })
        {
            foreach (var leak in new[] { "/data/", "relativePath", "size", "releaseTitle", "indexer", "qualityProfile", "rootFolder", "1080p", "Matrix.1999" })
            {
                Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Null(JsonSerializer.Deserialize<DiscoverMovieResponse>(movie, Json)!.LibraryId);
        Assert.Null(JsonSerializer.Deserialize<DiscoverSeriesResponse>(series, Json)!.LibraryId);
        Assert.Equal(AvailabilityState.Available, JsonSerializer.Deserialize<DiscoverMovieResponse>(movie, Json)!.Availability.Standard);

        // The search keeps people, drops adult titles; a person's credits merge cast and crew.
        var found = JsonSerializer.Deserialize<DiscoverPage>(search, Json)!;
        Assert.Equal([DiscoverMediaType.Person, DiscoverMediaType.Movie], found.Results.Select(r => r.MediaType));
        Assert.Null(found.Results[0].Availability);
        var keanu = JsonSerializer.Deserialize<DiscoverPersonResponse>(person, Json)!;
        Assert.Equal([603, 604], keanu.Credits.Select(c => c.TmdbId));
        Assert.Equal("Neo", keanu.Credits[0].Subtitle);

        // Row settings, lookups and the rest of Bigarr stay the admin's.
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/discover/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/discover/lookup?kind=studio&query=a24")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/movies")).StatusCode);

        // Anonymous: nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await env.Factory.CreateAnonymousClient().GetAsync("/api/v1/discover")).StatusCode);
    }

    [Fact]
    public async Task The_admin_reorders_switches_off_and_adds_rows_and_sets_the_4k_version()
    {
        await using var env = await Env.StartAsync();
        await env.SeedLibraryAsync();
        var user = await env.SignInUserAsync();

        var settings = await env.Admin.GetFromJsonAsync<DiscoverSettingsResponse>("/api/v1/discover/settings", Json);
        Assert.Equal("4k", settings!.FourKVersion);
        Assert.Equal(["1080p", "4k"], settings.VersionNames);
        Assert.All(settings.Sliders, s => Assert.True(s.BuiltIn && s.Enabled));

        // Popular films first, trending off, a custom genre row at the end; no 4K.
        var rows = settings.Sliders.Select(s => new UpdateDiscoverSliderRequest(s.Id, s.Kind, s.Title, s.Value, s.Kind != DiscoverSliderKind.Trending)).ToList();
        var popular = rows.Single(r => r.Kind == DiscoverSliderKind.PopularMovies);
        rows.Remove(popular);
        rows.Insert(0, popular);
        rows.Add(new UpdateDiscoverSliderRequest(null, DiscoverSliderKind.MovieGenre, "Animated films", "16", true));
        var saved = await env.Admin.PutAsJsonAsync("/api/v1/discover/settings", new UpdateDiscoverSettingsRequest("", rows), Json);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var home = await user.GetFromJsonAsync<DiscoverHomeResponse>("/api/v1/discover", Json);
        Assert.False(home!.FourK);
        Assert.Equal(DiscoverSliderKind.PopularMovies, home.Sliders[0].Kind);
        Assert.DoesNotContain(home.Sliders, s => s.Kind == DiscoverSliderKind.Trending);
        var custom = home.Sliders[^1];
        Assert.Equal(("Animated films", "16"), (custom.Title, custom.Value));

        var genre = await user.GetFromJsonAsync<DiscoverPage>($"/api/v1/discover/sliders/{custom.Id}", Json);
        Assert.Contains("with_genres=16", env.Tmdb.Requests.Last().Query);
        Assert.Null(genre!.Results.Single(r => r.TmdbId == 604).Availability!.FourK);

        // The switched-off row: hidden from the user, still previewed by the admin.
        var trendingId = settings.Sliders.Single(s => s.Kind == DiscoverSliderKind.Trending).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/discover/sliders/{trendingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.GetAsync($"/api/v1/discover/sliders/{trendingId}")).StatusCode);

        // A built-in row cannot be dropped nor added twice; a custom row needs a usable value.
        var withoutTrending = rows.Where(r => r.Kind != DiscoverSliderKind.Trending).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Admin.PutAsJsonAsync("/api/v1/discover/settings", new UpdateDiscoverSettingsRequest("4k", withoutTrending), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Admin.PutAsJsonAsync("/api/v1/discover/settings", new UpdateDiscoverSettingsRequest("4k", [.. rows, new(null, DiscoverSliderKind.Trending, null, null, true)]), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Admin.PutAsJsonAsync("/api/v1/discover/settings", new UpdateDiscoverSettingsRequest("4k", [.. rows, new(null, DiscoverSliderKind.Studio, "A24", "a24", true)]), Json)).StatusCode);

        // Dropping the custom row deletes it.
        var current = (await env.Admin.GetFromJsonAsync<DiscoverSettingsResponse>("/api/v1/discover/settings", Json))!.Sliders;
        var kept = current.Where(s => s.BuiltIn).Select(s => new UpdateDiscoverSliderRequest(s.Id, s.Kind, s.Title, s.Value, s.Enabled)).ToList();
        Assert.Equal(HttpStatusCode.OK, (await env.Admin.PutAsJsonAsync("/api/v1/discover/settings", new UpdateDiscoverSettingsRequest("4k", kept), Json)).StatusCode);
        using var scope = env.Factory.Services.CreateScope();
        Assert.Equal(DiscoverSlider.Defaults.Length, await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().DiscoverSliders.CountAsync());
    }

    [Fact]
    public async Task Tmdb_answers_are_reused_for_a_while_but_availability_is_read_every_time()
    {
        await using var env = await Env.StartAsync();
        var home = await env.Admin.GetFromJsonAsync<DiscoverHomeResponse>("/api/v1/discover", Json);
        var url = $"/api/v1/discover/sliders/{Id(home!, DiscoverSliderKind.Trending)}";

        var before = await env.Admin.GetFromJsonAsync<DiscoverPage>(url, Json);
        Assert.Equal(AvailabilityState.NotInLibrary, before!.Results[0].Availability!.Standard);

        await env.SeedLibraryAsync();
        var after = await env.Admin.GetFromJsonAsync<DiscoverPage>(url, Json);
        Assert.Equal(AvailabilityState.Available, after!.Results[0].Availability!.Standard);
        Assert.Single(env.Tmdb.Requests, r => r.AbsolutePath == "/3/trending/all/week");
    }

    [Fact]
    public async Task Without_a_tmdb_key_there_is_nothing_to_browse()
    {
        await using var factory = new BigarrApiFactory();
        var admin = factory.CreateClientWithCookies();
        await admin.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));

        var home = await admin.GetFromJsonAsync<DiscoverHomeResponse>("/api/v1/discover", Json);
        Assert.False(home!.Configured);
        Assert.Empty(home.Sliders);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync("/api/v1/discover/movies/603")).StatusCode);
    }

    private static int Id(DiscoverHomeResponse home, DiscoverSliderKind kind) => home.Sliders.Single(s => s.Kind == kind).Id;

    private sealed class Env : IAsyncDisposable
    {
        private Env(DiscoverFactory factory, HttpClient admin)
        {
            Factory = factory;
            Admin = admin;
        }

        public DiscoverFactory Factory { get; }

        public HttpClient Admin { get; }

        public RecordedTmdb Tmdb => Factory.Tmdb;

        public static async Task<Env> StartAsync()
        {
            var factory = new DiscoverFactory();
            var admin = factory.CreateClientWithCookies();
            var login = await admin.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            return new Env(factory, admin);
        }

        /// <summary>A user with the default permissions and a password, signed in.</summary>
        public async Task<HttpClient> SignInUserAsync()
        {
            using (var scope = Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
                var user = new User { Username = "demo-friend", PasswordHash = string.Empty, Role = UserRole.User, Permissions = UserPermissions.Defaults() };
                user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, "friend-password-1");
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            var client = Factory.CreateClientWithCookies();
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("demo-friend", "friend-password-1"));
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            return client;
        }

        /// <summary>
        /// The Matrix (603): 1080p with a file, 4k monitored without one. The Matrix Reloaded (604): its only version
        /// unmonitored, no file. Breaking Bad (1396 / TVDB 81189): 1080p has season 1, half of season 2 (plus an episode
        /// that has not aired), not the special; 4k wants season 1 and has nothing.
        /// </summary>
        public async Task<int> SeedLibraryAsync(int? seriesTmdbId = 1396)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var (matrix, hd) = await TestData.SeedMovieAsync(db, 603, "1080p");
            db.MediaFiles.Add(TestData.NewFile(hd, "The.Matrix.1999.1080p.mkv"));
            db.MovieVersions.Add(TestData.NewVersion(matrix, "4k"));
            var (_, reloaded) = await TestData.SeedMovieAsync(db, 604, "1080p");
            reloaded.Monitored = false;
            await db.SaveChangesAsync();

            var root = db.RootFolders.Add(new RootFolder { Path = "/data/tv", Kind = MediaKind.Series }).Entity;
            var series = new Bigarr.Domain.Tv.Series { TvdbId = 81189, TmdbId = seriesTmdbId, Title = "Breaking Bad", SortTitle = "breaking bad", DefaultRootFolder = root };
            var standard = new SeriesVersion { Series = series, Name = "1080p", IsDefault = true, QualityProfileId = hd.QualityProfileId, RootFolder = root, FolderName = "Breaking Bad (2008)" };
            var fourK = new SeriesVersion { Series = series, Name = "4k", QualityProfileId = hd.QualityProfileId, RootFolder = root, FolderName = "Breaking Bad (2008) 4k" };
            db.SeriesVersions.AddRange(standard, fourK);
            var seasons = new[] { 0, 1, 2 }.ToDictionary(n => n, n => new Season { Series = series, SeasonNumber = n });
            var aired = new DateTime(2009, 3, 8, 2, 0, 0, DateTimeKind.Utc);

            void Episode(int season, int number, bool hasAired, bool standardFile, bool standardMonitored, bool fourKMonitored)
            {
                var episode = new Episode { Series = series, Season = seasons[season], TvdbId = 1000 + (season * 100) + number, SeasonNumber = season, EpisodeNumber = number, Title = $"Episode {number}", AirDateUtc = hasAired ? aired : new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
                var file = standardFile ? new EpisodeFile { Version = standard, RelativePath = $"Season {season}/E{number}.mkv", SeasonNumber = season, QualityId = TestData.BlurayQualityId, Size = 1_000 } : null;
                db.EpisodeTargets.Add(new EpisodeTarget { Version = standard, Episode = episode, EpisodeFile = file, Monitored = standardMonitored });
                db.EpisodeTargets.Add(new EpisodeTarget { Version = fourK, Episode = episode, Monitored = fourKMonitored });
            }

            Episode(0, 1, hasAired: true, standardFile: false, standardMonitored: false, fourKMonitored: false);
            Episode(1, 1, hasAired: true, standardFile: true, standardMonitored: true, fourKMonitored: true);
            Episode(1, 2, hasAired: true, standardFile: true, standardMonitored: true, fourKMonitored: true);
            Episode(2, 1, hasAired: true, standardFile: true, standardMonitored: true, fourKMonitored: false);
            Episode(2, 2, hasAired: true, standardFile: false, standardMonitored: true, fourKMonitored: false);
            Episode(2, 3, hasAired: false, standardFile: false, standardMonitored: true, fourKMonitored: false);
            await db.SaveChangesAsync();
            return matrix.Id;
        }

        public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
    }

    private sealed class DiscoverFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Tmdb:ApiKey"] = "test-key" })
    {
        public RecordedTmdb Tmdb { get; } = new(new Dictionary<string, string>
        {
            ["/3/trending/all/week"] = DiscoverRecorded.TrendingWeek,
            ["/3/movie/popular"] = DiscoverRecorded.PopularMovies,
            ["/3/discover/movie"] = DiscoverRecorded.PopularMovies,
            ["/3/genre/movie/list"] = DiscoverRecorded.MovieGenres,
            ["/3/genre/tv/list"] = DiscoverRecorded.SeriesGenres,
            ["/3/movie/603"] = DiscoverRecorded.Movie603,
            ["/3/tv/1396"] = DiscoverRecorded.Tv1396,
            ["/3/search/multi"] = DiscoverRecorded.SearchMulti,
            ["/3/person/6384"] = DiscoverRecorded.Person6384,
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddHttpClient<TmdbDiscoverClient>().ConfigurePrimaryHttpMessageHandler(() => Tmdb));
        }
    }

    /// <summary>Answers recorded bodies by path (404 otherwise) and remembers every request.</summary>
    private sealed class RecordedTmdb(Dictionary<string, string> routes) : HttpMessageHandler
    {
        private readonly List<Uri> _requests = [];

        public IReadOnlyList<Uri> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(request.RequestUri!);
            }

            var found = routes.TryGetValue(request.RequestUri!.AbsolutePath, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(found ? body! : """{ "success": false, "status_code": 34 }""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
