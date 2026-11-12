using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Auth;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;

namespace Bigarr.Api.Tests.Compat;

/// <summary>What the compat tests share: a session to create keys, one HTTP client per key, and Radarr-shaped request bodies.</summary>
internal static class CompatTestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<HttpClient> SessionAsync(BigarrApiFactory factory)
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    /// <summary>Creates a key with the given view and returns a client that sends it the way Seerr does: in the query string.</summary>
    public static async Task<(HttpClient Client, string Key)> KeyClientAsync(BigarrApiFactory factory, HttpClient session, string name, string view, bool header = false)
    {
        var response = await session.PostAsJsonAsync("/api/v1/apikeys", new CreateApiKeyRequest(name, view));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>(Json))!;

        var client = factory.CreateAnonymousClient();
        if (header)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", created.Key);
        }
        else
        {
            client = new HttpClient(new QueryKeyHandler(created.Key, factory.Server.CreateHandler())) { BaseAddress = factory.Server.BaseAddress };
        }

        return (client, created.Key);
    }

    public static async Task<(int Hd, int Uhd, RootFolderResponse Root)> SetupAsync(HttpClient session)
    {
        var profiles = (await session.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var roots = (await session.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!;
        if (roots.Count == 0)
        {
            var dir = Directory.CreateTempSubdirectory("bigarr-compat-lib-").FullName;
            var created = await session.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            roots = [(await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!];
        }

        return (profiles.Single(p => p.Name == QualitySeeder.HdProfileName).Id, profiles.Single(p => p.Name == QualitySeeder.UhdProfileName).Id, roots[0]);
    }

    /// <summary>The body Seerr posts, field for field.</summary>
    public static object SeerrAdd(int tmdbId, string title, int year, int profileId, string rootFolderPath, bool monitored = true, bool search = false) => new
    {
        title,
        qualityProfileId = profileId,
        profileId,
        titleSlug = tmdbId.ToString(),
        minimumAvailability = "released",
        tmdbId,
        year,
        rootFolderPath,
        monitored,
        tags = Array.Empty<int>(),
        addOptions = new { searchForMovie = search },
    };

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Appends <c>?apikey=</c> to every request, like Seerr's and Bazarr's HTTP clients.</summary>
    private sealed class QueryKeyHandler(string key, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
            request.RequestUri = new Uri(uri + separator + "apikey=" + Uri.EscapeDataString(key));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
