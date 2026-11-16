using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Requests;
using Bigarr.Api.Infrastructure.Metadata.Fixtures;
using Bigarr.Api.Infrastructure.Metadata.Tmdb;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Discover;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Library;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Requests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.Requests;

/// <summary>
/// A Bigarr with Discover on recorded TMDB answers (films 603 and 604, Breaking Bad 1396 with seasons 0–3), the library's
/// metadata from the embedded fixtures, a film and a series root folder, and the admin's session. Demo data only.
/// </summary>
internal sealed class RequestsEnv : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public const string Movie604 = """
    { "id": 604, "imdb_id": "tt0234215", "title": "The Matrix Reloaded", "original_title": "The Matrix Reloaded", "release_date": "2003-05-15",
      "runtime": 138, "overview": "Six months after the events depicted in The Matrix.", "poster_path": "/9TGHDvWrqKBzwDxDodHYXEmOE6J.jpg",
      "genres": [], "production_companies": [], "vote_average": 7.0 }
    """;

    private RequestsEnv(Factory factory, HttpClient admin)
    {
        App = factory;
        Admin = admin;
    }

    public BigarrApiFactory App { get; }

    public HttpClient Admin { get; }

    public int FilmRootId { get; private set; }

    public int SeriesRootId { get; private set; }

    public static async Task<RequestsEnv> StartAsync(bool shadow = false, bool seriesRoot = true, TimeProvider? time = null)
    {
        var settings = new Dictionary<string, string> { ["Bigarr:Tmdb:ApiKey"] = "test-key" };
        if (shadow)
        {
            settings["Bigarr:Shadow:Enabled"] = "true";
        }

        var factory = new Factory(settings, time);
        var admin = factory.CreateClientWithCookies();
        var login = await admin.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(BigarrApiFactory.AdminUsername, BigarrApiFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var env = new RequestsEnv(factory, admin);
        env.FilmRootId = await env.AddRootAsync(MediaKind.Movies);
        if (seriesRoot)
        {
            env.SeriesRootId = await env.AddRootAsync(MediaKind.Series);
        }

        return env;
    }

    public async Task<int> AddRootAsync(MediaKind kind)
    {
        var path = Directory.CreateTempSubdirectory($"bigarr-requests-{kind}-").FullName;
        var response = await Admin.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path, kind), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
        if (kind == MediaKind.Series)
        {
            SeriesRootId = root.Id;
        }

        return root.Id;
    }

    public async Task<int> ProfileAsync(string name)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.QualityProfiles.Where(p => p.Name == name).Select(p => p.Id).SingleAsync();
    }

    public Task<int> HdAsync() => ProfileAsync(QualitySeeder.HdProfileName);

    public Task<int> SeriesHdAsync() => ProfileAsync(QualitySeeder.SeriesHdProfileName);

    /// <summary>A signed-in user (password login) with the given role, permissions and quota.</summary>
    public async Task<HttpClient> UserAsync(string name, UserPermissions permissions, UserRole role = UserRole.User, RequestQuota? quota = null)
    {
        await using (var scope = App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var user = new User { Username = name, PasswordHash = string.Empty, Role = role, Permissions = permissions, Quota = quota ?? new RequestQuota() };
            user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, "demo-password-1");
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var client = App.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(name, "demo-password-1"));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public static Task<HttpResponseMessage> AskAsync(HttpClient client, RequestMediaType type, int tmdbId, bool fourK = false, List<int>? seasons = null) =>
        client.PostAsJsonAsync("/api/v1/requests", new CreateRequestRequest(type, tmdbId, fourK, seasons), Json);

    public static async Task<MediaRequestResponse> AskOkAsync(HttpClient client, RequestMediaType type, int tmdbId, bool fourK = false, List<int>? seasons = null)
    {
        var response = await AskAsync(client, type, tmdbId, fourK, seasons);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!;
    }

    public async Task<MediaRequestResponse> ApproveAsync(HttpClient client, int id, ApproveRequestRequest? edits = null)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/requests/{id}/approve", edits ?? new ApproveRequestRequest(), Json);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MediaRequestResponse>(Json))!;
    }

    public static async Task<MediaRequestResponse> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<MediaRequestResponse>($"/api/v1/requests/{id}", Json))!;

    public async Task<T> DbAsync<T>(Func<BigarrDbContext, Task<T>> read)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BigarrDbContext>());
    }

    public Task<List<string>> OutboxTypesAsync() => DbAsync(db => db.Outbox.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Type).ToListAsync());

    public async Task<string?> RunJobAsync(string name)
    {
        var response = await Admin.PostAsync($"/api/v1/system/tasks/{name}/run", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync() => await App.DisposeAsync();

    private sealed class Factory(IReadOnlyDictionary<string, string> settings, TimeProvider? time) : BigarrApiFactory(null, settings, time: time)
    {
        private readonly Recorded _tmdb = new(new Dictionary<string, string>
        {
            ["/3/movie/603"] = DiscoverRecorded.Movie603,
            ["/3/movie/604"] = Movie604,
            ["/3/tv/1396"] = DiscoverRecorded.Tv1396,
            ["/3/find/tt0133093"] = """{ "movie_results": [{ "id": 603, "title": "The Matrix", "release_date": "1999-03-30", "poster_path": "/m.jpg" }], "tv_results": [] }""",
            ["/3/find/81189"] = """{ "movie_results": [], "tv_results": [{ "id": 1396, "name": "Breaking Bad", "first_air_date": "2008-01-20" }] }""",
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<TmdbDiscoverClient>().ConfigurePrimaryHttpMessageHandler(() => _tmdb);

                // Discover reads the recorded TMDB; the library adds from the embedded fixtures, as without a key.
                services.AddSingleton<IMetadataProvider, FixtureMetadataProvider>();
                services.AddSingleton<ISeriesMetadataProvider, FixtureSeriesMetadataProvider>();
            });
        }
    }

    private sealed class Recorded(Dictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var found = routes.TryGetValue(request.RequestUri!.AbsolutePath, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(found ? body! : """{ "success": false, "status_code": 34 }""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

internal static class RequestsEnvFiles
{
    /// <summary>Gives every episode of a season of the version a file.</summary>
    public static Task GiveFilesAsync(this RequestsEnv env, int versionId, int season) => env.DbAsync(async db =>
    {
        var version = await db.SeriesVersions.SingleAsync(v => v.Id == versionId);
        var targets = await db.EpisodeTargets.Include(t => t.Episode).Where(t => t.VersionId == versionId && t.Episode.SeasonNumber == season).ToListAsync();
        foreach (var target in targets)
        {
            target.EpisodeFile = new Bigarr.Domain.Tv.EpisodeFile { Version = version, RelativePath = $"Season {season}/E{target.Episode.EpisodeNumber}.mkv", SeasonNumber = season, QualityId = TestDataIds.Bluray, Size = 1_000 };
        }

        return await db.SaveChangesAsync();
    });
}

internal static class TestDataIds
{
    public static int Bluray => Bigarr.Api.Tests.Persistence.TestData.BlurayQualityId;
}
