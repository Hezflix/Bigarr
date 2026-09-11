using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Decision;

namespace Bigarr.Api.Tests.Quality;

/// <summary>Shared plumbing of the quality tests: login, seeded profiles, guide imports, a movie to search for.</summary>
public static class QualityTestClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<HttpClient> LoginAsync(BigarrApiFactory factory)
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public static async Task<QualityProfileSummary> ProfileAsync(HttpClient client, string name)
    {
        var profiles = await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json);
        return profiles!.Single(p => p.Name == name);
    }

    /// <summary>Imports guide files unchanged, optionally applying their default score to profiles.</summary>
    public static async Task<CustomFormatImportResponse> ImportAsync(HttpClient client, IEnumerable<string> files, params int[] profileIds)
    {
        var body = "[" + string.Join(",", files.Select(TrashFixtures.Json)) + "]";
        var query = profileIds.Length == 0 ? string.Empty : "?profileIds=" + string.Join(",", profileIds);
        var response = await client.PostAsync("/api/v1/customformats/import" + query, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomFormatImportResponse>(Json))!;
    }

    public static async Task<int> RootFolderAsync(HttpClient client)
    {
        var roots = await client.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json);
        if (roots!.Count > 0)
        {
            return roots[0].Id;
        }

        var dir = Directory.CreateTempSubdirectory("bigarr-quality-").FullName;
        var created = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(dir));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!.Id;
    }

    public static async Task<MovieResponse> AddMovieAsync(HttpClient client, int tmdbId, int profileId, int rootId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }
}
