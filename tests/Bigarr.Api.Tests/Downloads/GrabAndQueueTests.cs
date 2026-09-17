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
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Tests.Downloads;

/// <summary>The M4 acceptance loop on the mock client: grab → queue → downloading; removed by hand → removed. Completion and import live in the Import tests.</summary>
public sealed class GrabAndQueueTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task A_grab_flows_through_the_queue_and_leaves_it_when_the_torrent_vanishes()
    {
        var client = await ClientAsync();
        var (profileId, rootId) = await SetupAsync(client);
        var movie = await AddMovieAsync(client, 603, profileId, rootId);
        var version = movie.Versions.Single();
        var indexer = await AddMockIndexerAsync(client, "Mock grab");

        // Without a client the grab is refused before anything is sent.
        var search = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
        var best = search.Releases.First(r => r.Versions.Any(v => v.Approved));
        var noClient = await client.PostAsJsonAsync("/api/v1/grab", GrabFor(best, version.Id), Json);
        Assert.Equal(HttpStatusCode.BadRequest, noClient.StatusCode);
        Assert.Contains("No download client", await noClient.Content.ReadAsStringAsync());

        var downloadClient = await AddMockClientAsync(client, "Mock qbt");
        try
        {
            var grabbed = await client.PostAsJsonAsync("/api/v1/grab", GrabFor(best, version.Id), Json);
            Assert.Equal(HttpStatusCode.Created, grabbed.StatusCode);
            var grab = (await grabbed.Content.ReadFromJsonAsync<GrabResponse>(Json))!;
            Assert.Equal(GrabState.Grabbed, grab.State);
            Assert.Equal(best.InfoHash, grab.DownloadId);
            Assert.Equal(downloadClient.Id, grab.DownloadClientId);

            // The version is downloading, the journal knows why, and a second grab is refused.
            var refreshed = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
            Assert.Equal(VersionStatus.Downloading, refreshed.Versions.Single().Status);

            var history = (await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?movieId={movie.Id}", Json))!;
            var entry = Assert.Single(history);
            Assert.Equal(HistoryEventType.Grabbed, entry.EventType);
            Assert.Equal(best.Title, entry.SourceTitle);
            Assert.Equal(version.Id, entry.VersionId);
            Assert.Equal("The Matrix", entry.MovieTitle);
            Assert.Equal(indexer.Id, entry.IndexerId);
            Assert.Equal("Mock qbt", entry.Data["downloadClient"]);
            Assert.Equal("Remux-1080p", entry.Data["quality"]);

            var again = await client.PostAsJsonAsync("/api/v1/grab", GrabFor(best, version.Id), Json);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

            var judged = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            Assert.All(judged.Releases.Where(r => r.MatchesMovie), r =>
                Assert.Contains(r.Versions.Single().Rejections, x => x.Reason == "A download is already active for this version" && x.Temporary));

            // First poll: the client lists it, the grab moves to downloading.
            var queue = await RefreshAsync(client);
            var row = Assert.Single(queue.Items);
            Assert.True(row.Tracked);
            Assert.Equal(grab.GrabId, row.GrabId);
            Assert.Equal(GrabState.Downloading, row.State);
            Assert.Equal(DownloadItemStatus.Downloading, row.Status);
            Assert.Equal(movie.Id, row.MovieId);
            Assert.Equal("The Matrix", row.MovieTitle);
            Assert.Equal(1999, row.MovieYear);
            Assert.Equal(version.Id, row.VersionId);
            Assert.Equal("1080p", row.VersionName);
            Assert.Equal("Remux-1080p", row.Quality);
            Assert.Equal("Mock qbt", row.DownloadClient);
            Assert.Equal("Mock grab", row.Indexer);
            Assert.True(row.Size > 0);
            Assert.InRange(row.Progress, 0, 1);
            Assert.True(queue.Revision >= 1);
            Assert.Single(queue.Clients, c => c.DownloadClientId == downloadClient.Id && c.Healthy);

            var snapshot = (await client.GetFromJsonAsync<QueueResponse>("/api/v1/queue", Json))!;
            Assert.Equal(queue.Revision, snapshot.Revision);

            // Deleted by hand in the client while downloading: gone after N polls, the version is missing again, no blocklist.
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/downloadclients/mock/{grab.DownloadId}")).StatusCode);
            queue = await RefreshAsync(client);
            row = Assert.Single(queue.Items);
            Assert.Equal(GrabState.Downloading, row.State);
            Assert.Contains(row.StatusMessages, m => m.Title == "Not in the download client" && m.Messages[0].Contains("(1/3)"));
            await RefreshAsync(client);
            queue = await RefreshAsync(client);
            Assert.Empty(queue.Items);

            refreshed = (await client.GetFromJsonAsync<MovieResponse>($"/api/v1/movies/{movie.Id}", Json))!;
            Assert.Equal(VersionStatus.Missing, refreshed.Versions.Single().Status);
            var afterRemoval = (await client.GetFromJsonAsync<SearchResponse>($"/api/v1/search?movieId={movie.Id}", Json))!;
            Assert.True(afterRemoval.Releases.First(r => r.Title == best.Title).Versions.Single().Approved);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
            await client.DeleteAsync($"/api/v1/movies/{movie.Id}");
        }
    }

    [Fact]
    public async Task The_queue_stream_sends_the_snapshot_then_every_poll()
    {
        var client = await ClientAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/queue/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        var first = await ReadEventAsync(reader, cts.Token);
        Assert.Equal("queue", first.Type);
        var initial = JsonSerializer.Deserialize<QueueResponse>(first.Data, Json)!;
        Assert.NotNull(initial.Items);

        var polled = await RefreshAsync(client);
        var second = await ReadEventAsync(reader, cts.Token);
        var pushed = JsonSerializer.Deserialize<QueueResponse>(second.Data, Json)!;
        Assert.Equal(polled.Revision, pushed.Revision);
        Assert.True(pushed.Revision > initial.Revision);
        Assert.NotNull(pushed.LastPollAt);
    }

    private static async Task<(string Type, string Data)> ReadEventAsync(StreamReader reader, CancellationToken ct)
    {
        string type = "message";
        var data = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new InvalidOperationException("stream ended");
            if (line.Length == 0)
            {
                if (data.Count > 0)
                {
                    return (type, string.Join('\n', data));
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                type = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line["data:".Length..].TrimStart());
            }
        }
    }

    private static GrabRequest GrabFor(ReleaseResponse release, int versionId) => new(
        versionId, release.IndexerId, release.Guid, release.Title, release.DownloadUrl, release.MagnetUrl, release.InfoHash,
        release.Size, release.Seeders, release.Flags, release.Protocol, release.Indexer);

    private static async Task<QueueResponse> RefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/queue/refresh", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QueueResponse>(Json))!;
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
            var dir = Directory.CreateTempSubdirectory("bigarr-queue-").FullName;
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

    private static async Task<IndexerResponse> AddMockIndexerAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/indexers", new IndexerRequest(name, IndexerImplementation.Mock, MinimumSeeders: 1, RateLimitSeconds: 0, SeedRatio: 1.0), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IndexerResponse>(Json))!;
    }

    private static async Task<DownloadClientResponse> AddMockClientAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest(name, DownloadClientImplementation.Mock, Category: name.Replace(' ', '-')), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
    }
}
