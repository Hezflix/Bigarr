using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Shadow;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using static Bigarr.Api.Tests.Requests.RequestsEnv;

namespace Bigarr.Api.Tests.Requests;

/// <summary>
/// The admin adds titles from Discover, through the request path (an auto-approved request in their name, forwarded
/// like any other), with every option the library's add offers; a series TMDB lacks comes by its TVDB id.
/// </summary>
public sealed class AdminAddTests
{
    private static Task<HttpResponseMessage> AddAsync(HttpClient client, RequestMediaType type, int? tmdbId, AdminAddRequest add, List<int>? seasons = null, int? tvdbId = null) =>
        client.PostAsJsonAsync("/api/v1/requests", new CreateRequestRequest(type, tmdbId, Seasons: seasons, TvdbId: tvdbId, Add: add), Json);

    private static async Task<MediaRequestResponse> AddOkAsync(HttpClient client, RequestMediaType type, int? tmdbId, AdminAddRequest add, List<int>? seasons = null, int? tvdbId = null)
    {
        var response = await AddAsync(client, type, tmdbId, add, seasons, tvdbId);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!;
    }

    [Fact]
    public async Task The_admin_adds_a_film_with_the_library_add_options_as_an_approved_request_in_their_name()
    {
        await using var env = await StartAsync();
        var uhd = await env.ProfileAsync("4K");
        var added = await AddOkAsync(env.Admin, RequestMediaType.Movie, 603, new AdminAddRequest(uhd, env.FilmRootId, "remux", Monitored: false, SearchOnAdd: false, MinimumAvailability: MinimumAvailability.InCinemas));
        Assert.Equal(RequestStatus.Approved, added.Status);
        Assert.True(added.AutoApproved);
        Assert.Equal("admin", added.RequestedBy!.Username);
        Assert.NotNull(added.LibraryId);

        var version = await env.DbAsync(db => db.MovieVersions.SingleAsync(v => v.Movie.TmdbId == 603));
        Assert.Equal("remux", version.Name);
        Assert.Equal(uhd, version.QualityProfileId);
        Assert.False(version.Monitored);
        Assert.Equal(MinimumAvailability.InCinemas, version.MinimumAvailability);
        Assert.Contains("requestApproved", await env.OutboxTypesAsync());

        // Another version of a film the library has; the same name twice is refused before anything happens.
        await AddOkAsync(env.Admin, RequestMediaType.Movie, 603, new AdminAddRequest(VersionName: "directors-cut"));
        Assert.Equal(["directors-cut", "remux"], await env.DbAsync(db => db.MovieVersions.Where(v => v.Movie.TmdbId == 603).OrderBy(v => v.Name).Select(v => v.Name).ToListAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await AddAsync(env.Admin, RequestMediaType.Movie, 603, new AdminAddRequest(VersionName: "remux"))).StatusCode);

        // A version named like the 4K setting lands in the 4K slot.
        var fourK = await AddOkAsync(env.Admin, RequestMediaType.Movie, 603, new AdminAddRequest(VersionName: "4k"));
        Assert.True(fourK.FourK);
    }

    [Fact]
    public async Task The_admins_add_is_forwarded_to_the_followed_radarr_of_its_version()
    {
        await using var env = await StartAsync();
        await using var radarr = await FakeHttpServer.StartAsync();
        radarr.Responder = r => (r.Method, r.Path) switch
        {
            ("GET", "/api/v3/movie/lookup/tmdb") => new FakeHttpServer.Answer(200, """{ "title": "The Matrix", "year": 1999, "tmdbId": 603 }"""),
            ("POST", "/api/v3/movie") => new FakeHttpServer.Answer(201, """{ "id": 41 }"""),
            _ => new FakeHttpServer.Answer(404),
        };
        await env.FollowRadarrAsync(radarr.Url, "1080p", 7, await env.HdAsync(), "/radarr-movies", env.FilmRootId);

        var added = await AddOkAsync(env.Admin, RequestMediaType.Movie, 603, new AdminAddRequest(await env.HdAsync(), env.FilmRootId));
        Assert.Equal(ForwardStatus.Forwarded, added.ForwardStatus);
        Assert.Single(radarr.Requests, r => r.Method == "POST" && r.Path == "/api/v3/movie");
    }

    [Fact]
    public async Task The_admin_adds_a_series_with_a_monitoring_rule_type_and_folders()
    {
        await using var env = await StartAsync();
        var added = await AddOkAsync(env.Admin, RequestMediaType.Series, 1396, new AdminAddRequest(
            await env.SeriesHdAsync(), env.SeriesRootId, SeriesType: SeriesType.Anime, SuggestedSeriesType: false, Monitor: MonitoringOption.FirstSeason, SeasonFolder: false, MonitorNewItems: NewItemMonitoring.None, SearchOnAdd: false));
        Assert.Equal(RequestStatus.Approved, added.Status);
        Assert.Equal([1], added.Seasons); // what the rule monitored

        var (type, version) = await env.DbAsync(async db =>
        {
            var series = await db.Series.SingleAsync(s => s.TmdbId == 1396);
            return (series.SeriesType, await db.SeriesVersions.Include(v => v.Seasons).ThenInclude(s => s.Season).SingleAsync(v => v.SeriesId == series.Id));
        });
        Assert.Equal(SeriesType.Anime, type);
        Assert.False(version.SeasonFolder);
        Assert.Equal(NewItemMonitoring.None, version.MonitorNewItems);
        Assert.Equal([1], version.Seasons.Where(s => s.Monitored).Select(s => s.Season.SeasonNumber).Order());
    }

    [Fact]
    public async Task A_series_only_tvdb_knows_is_added_by_the_admin_from_its_tvdb_id()
    {
        await using var env = await StartAsync();
        var user = await env.UserAsync("demo-friend", UserPermissions.Defaults());

        // Only the admin, and only with the add options, may come without a TMDB id or with options.
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(user, RequestMediaType.Series, null, new AdminAddRequest(), tvdbId: 81189)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(user, RequestMediaType.Movie, 603, new AdminAddRequest(VersionName: "remux"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(env.Admin, RequestMediaType.Movie, null, new AdminAddRequest())).StatusCode);

        var added = await AddOkAsync(env.Admin, RequestMediaType.Series, null, new AdminAddRequest(Monitor: MonitoringOption.All, SearchOnAdd: false), tvdbId: 81189);
        Assert.Null(added.TmdbId);
        Assert.Equal(RequestStatus.Approved, added.Status);
        Assert.Equal([1, 2, 3, 4, 5], added.Seasons);
        Assert.True(await env.DbAsync(db => db.Series.AnyAsync(s => s.TvdbId == 81189)));
    }

    [Fact]
    public async Task The_admin_sees_root_folder_paths_in_the_options_a_manager_sees_folder_names()
    {
        await using var env = await StartAsync();
        var manager = await env.UserAsync("demo-manager", UserPermissions.Defaults(), UserRole.RequestManager);
        var admin = (await env.Admin.GetFromJsonAsync<RequestOptionsResponse>("/api/v1/requests/options?mediaType=movie", Json))!;
        var managed = (await manager.GetFromJsonAsync<RequestOptionsResponse>("/api/v1/requests/options?mediaType=movie", Json))!;
        Assert.StartsWith("/", admin.RootFolders.Single().Name, StringComparison.Ordinal);
        Assert.DoesNotContain("/", managed.RootFolders.Single().Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discover_search_takes_the_ids_the_former_add_pages_took()
    {
        await using var env = await StartAsync();
        async Task<List<(Bigarr.Api.Features.Discover.DiscoverMediaType, int)>> SearchAsync(string q) =>
            [.. (await env.Admin.GetFromJsonAsync<Bigarr.Api.Features.Discover.DiscoverPage>($"/api/v1/discover/search?query={Uri.EscapeDataString(q)}", Json))!.Results.Select(r => (r.MediaType, r.TmdbId))];

        Assert.Equal([(Bigarr.Api.Features.Discover.DiscoverMediaType.Movie, 603)], await SearchAsync("tmdb:603"));
        Assert.Equal([(Bigarr.Api.Features.Discover.DiscoverMediaType.Series, 1396)], await SearchAsync("tmdb:1396"));
        Assert.Equal([(Bigarr.Api.Features.Discover.DiscoverMediaType.Movie, 603)], await SearchAsync("tt0133093"));
        Assert.Equal([(Bigarr.Api.Features.Discover.DiscoverMediaType.Movie, 603)], await SearchAsync("imdb:tt0133093"));
        Assert.Equal([(Bigarr.Api.Features.Discover.DiscoverMediaType.Series, 1396)], await SearchAsync("tvdb:81189"));
    }
}
