using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Movies;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Tests.Import;

/// <summary>The HTTP choreography the import tests share: login, seed data, grab, poll, and wait for the import worker.</summary>
internal static class ImportTestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(30);

    public static async Task<HttpClient> ClientAsync(BigarrApiFactory factory)
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public static async Task<(int Hd, int Uhd)> ProfilesAsync(HttpClient client)
    {
        var profiles = (await client.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        return (profiles.Single(p => p.Name == QualitySeeder.HdProfileName).Id, profiles.Single(p => p.Name == QualitySeeder.UhdProfileName).Id);
    }

    public static async Task<RootFolderResponse> AddRootAsync(HttpClient client, string path)
    {
        Directory.CreateDirectory(path);
        var response = await client.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(path));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }

    public static async Task<MovieResponse> AddMovieAsync(HttpClient client, int tmdbId, int profileId, int rootId)
    {
        var response = await client.PostAsJsonAsync("/api/v1/movies", new AddMovieRequest(tmdbId, profileId, rootId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovieResponse>(Json))!;
    }

    public static async Task<VersionResponse> AddVersionAsync(HttpClient client, int movieId, AddVersionRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/movies/{movieId}/versions", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VersionResponse>(Json))!;
    }

    public static Task<MovieResponse> GetMovieAsync(HttpClient client, int movieId) =>
        client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movieId}", Json)!;

    public static async Task<IndexerResponse> AddMockIndexerAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest(name, IndexerImplementation.Mock, MinimumSeeders: 1, RateLimitSeconds: 0, SeedRatio: 1.0), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
    }

    public static async Task<DownloadClientResponse> AddMockClientAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest(name, DownloadClientImplementation.Mock, Category: name.Replace(' ', '-')), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
    }

    public static Task<SearchResponse> SearchAsync(HttpClient client, int movieId) =>
        client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movieId}", Json)!;

    public static async Task<GrabResponse> GrabAsync(HttpClient client, ReleaseResponse release, int versionId)
    {
        var request = new GrabRequest(
            versionId, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash,
            release.Size, release.Seeders, release.Flags, release.Protocol, release.Indexer);
        var response = await client.PostAsJsonAsync("/api/v1/grab", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
    }

    public static async Task CompleteAsync(HttpClient client, string downloadId) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/downloadclients/mock/{downloadId}/complete", null)).StatusCode);

    public static async Task<QueueResponse> RefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/queue/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QueueResponse>(Json))!;
    }

    public static Task<List<HistoryEventResponse>> HistoryAsync(HttpClient client, int movieId, HistoryEventType? type = null) =>
        client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?movieId={movieId}{(type is null ? string.Empty : $"&eventType={type}")}", Json)!;

    /// <summary>Grab, complete in the mock client, poll once: the import worker takes it from there.</summary>
    public static async Task<GrabResponse> GrabAndCompleteAsync(HttpClient client, ReleaseResponse release, int versionId)
    {
        var grab = await GrabAsync(client, release, versionId);
        await RefreshAsync(client);
        await CompleteAsync(client, grab.DownloadId);
        await RefreshAsync(client);
        return grab;
    }

    /// <summary>Polls the film until the version has a file matching <paramref name="until"/>. The worker runs in the test host.</summary>
    public static async Task<VersionResponse> WaitForFileAsync(HttpClient client, int movieId, int versionId, Func<VersionResponse, bool>? until = null)
    {
        var deadline = DateTime.UtcNow + ImportTimeout;
        while (true)
        {
            var movie = await GetMovieAsync(client, movieId);
            var version = movie.Versions.Single(v => v.Id == versionId);
            if (version.File is not null && (until is null || until(version)))
            {
                return version;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Version {versionId} of movie {movieId} got no file in {ImportTimeout}. {await DiagnosticsAsync(client, movieId)}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Polls the queue (a manual refresh each time: the tracker loop is off in tests) until a row matches.</summary>
    public static async Task<QueueItemResponse> WaitForQueueRowAsync(HttpClient client, Func<QueueItemResponse, bool> match, string what)
    {
        var deadline = DateTime.UtcNow + ImportTimeout;
        while (true)
        {
            var queue = await RefreshAsync(client);
            var row = queue.Items.FirstOrDefault(match);
            if (row is not null)
            {
                return row;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No queue row matched ({what}) in {ImportTimeout}. Rows: {string.Join(" | ", queue.Items.Select(i => $"{i.Title} {i.State} [{string.Join("; ", i.StatusMessages.Select(m => m.Title + ": " + string.Join(" ", m.Messages)))}]"))}");
            }

            await Task.Delay(100);
        }
    }

    private static async Task<string> DiagnosticsAsync(HttpClient client, int movieId)
    {
        var queue = await RefreshAsync(client);
        var history = await HistoryAsync(client, movieId);
        return $"Queue: {string.Join(" | ", queue.Items.Select(i => $"{i.Title} {i.State} [{string.Join("; ", i.StatusMessages.Select(m => m.Title + ": " + string.Join(" ", m.Messages)))}]"))}. History: {string.Join(" | ", history.Select(h => $"{h.EventType} {h.SourceTitle} {string.Join(",", h.Data.Select(d => d.Key + "=" + d.Value))}"))}";
    }

    /// <summary>Polls <paramref name="probe"/> until it returns a value; background workers run in the test host.</summary>
    public static async Task<T> WaitUntilAsync<T>(Func<Task<T?>> probe, string what, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? ImportTimeout);
        while (true)
        {
            var value = await probe();
            if (value is not null)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(100);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
