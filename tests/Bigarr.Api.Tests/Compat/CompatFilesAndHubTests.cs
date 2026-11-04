using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Tests.Auth;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>Bazarr's side of M7: the <c>all</c> key lists files as Radarr would, and hears about changes over SignalR, filtered by view.</summary>
public sealed class CompatFilesAndHubTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>, IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bigarr-compat-files-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public async Task A_bazarr_key_lists_the_file_and_the_queue_and_history_speak_radarr()
    {
        var session = await SessionAsync(factory);
        var (hd, _) = await ProfilesAsync(session);
        var root = await AddRootAsync(session, _root);
        var (bazarr, _) = await KeyClientAsync(factory, session, "bazarr-files", "all");

        var movie = await AddMovieAsync(session, 157336, hd, root.Id);
        var version = movie.Versions.Single();
        await AddMockIndexerAsync(session, "Mock compat idx");
        await AddMockClientAsync(session, "Mock compat qbt");

        var search = await SearchAsync(session, movie.Id);
        var release = search.Releases.First(r => r.Versions.Any(v => v.VersionId == version.Id && v.Approved) && r.Title.Contains("1080p", StringComparison.Ordinal));
        var grab = await GrabAsync(session, release, version.Id);
        await RefreshAsync(session);

        // While it downloads: one queue record, keyed by the version id, in Radarr's states.
        var queue = await (await bazarr.GetAsync("/api/v3/queue?page=1&pageSize=50")).ReadJsonAsync();
        var record = queue.GetProperty("records").EnumerateArray().Single(r => r.GetProperty("movieId").GetInt32() == version.Id);
        Assert.Equal(grab.GrabId, record.GetProperty("id").GetInt32());
        Assert.Equal(release.Title, record.GetProperty("title").GetString());
        Assert.Equal("downloading", record.GetProperty("trackedDownloadState").GetString());
        Assert.Equal("ok", record.GetProperty("trackedDownloadStatus").GetString());
        Assert.Equal("torrent", record.GetProperty("protocol").GetString());
        Assert.Equal(grab.DownloadId, record.GetProperty("downloadId").GetString());
        Assert.Contains("1080p", record.GetProperty("quality").GetProperty("quality").GetProperty("name").GetString());

        await CompleteAsync(session, grab.DownloadId);
        await RefreshAsync(session);
        var imported = await WaitForFileAsync(session, movie.Id, version.Id);

        var row = await (await bazarr.GetAsync($"/api/v3/movie/{version.Id}")).ReadJsonAsync();
        Assert.True(row.GetProperty("hasFile").GetBoolean());
        Assert.Equal(imported.File!.Size, row.GetProperty("sizeOnDisk").GetInt64());
        var file = row.GetProperty("movieFile");
        Assert.Equal(imported.File.Id, file.GetProperty("id").GetInt32());
        Assert.Equal(version.Id, file.GetProperty("movieId").GetInt32());
        Assert.Equal(imported.File.RelativePath, file.GetProperty("relativePath").GetString());
        Assert.Equal(Path.Combine(row.GetProperty("path").GetString()!, imported.File.RelativePath), file.GetProperty("path").GetString()); // Bazarr joins path + relativePath
        Assert.Equal(imported.File.Quality, file.GetProperty("quality").GetProperty("quality").GetProperty("name").GetString());
        Assert.Equal(1080, file.GetProperty("quality").GetProperty("quality").GetProperty("resolution").GetInt32());
        Assert.Equal("English", file.GetProperty("languages")[0].GetProperty("name").GetString());
        Assert.Equal(1, file.GetProperty("languages")[0].GetProperty("id").GetInt32());
        Assert.Equal(release.Title, file.GetProperty("sceneName").GetString());
        Assert.Equal(imported.File.HasMediaInfo, file.TryGetProperty("mediaInfo", out var mediaInfo));
        if (imported.File.HasMediaInfo)
        {
            Assert.Equal("320x240", mediaInfo.GetProperty("resolution").GetString());
        }

        // The journal in Radarr's vocabulary: eventType=1 keeps only the grab, whose data names the indexer and the group.
        var grabbed = await (await bazarr.GetAsync($"/api/v3/history?eventType=1&movieIds={version.Id}")).ReadJsonAsync();
        var entry = Assert.Single(grabbed.GetProperty("records").EnumerateArray());
        Assert.Equal("grabbed", entry.GetProperty("eventType").GetString());
        Assert.Equal(version.Id, entry.GetProperty("movieId").GetInt32());
        Assert.Equal(release.Title, entry.GetProperty("sourceTitle").GetString());
        Assert.Equal("Mock compat idx", entry.GetProperty("data").GetProperty("indexer").GetString());
        var everything = await (await bazarr.GetAsync($"/api/v3/history?movieIds={version.Id}&pageSize=10")).ReadJsonAsync();
        Assert.Contains(everything.GetProperty("records").EnumerateArray(), r => r.GetProperty("eventType").GetString() == "downloadFolderImported");
        var perMovie = await (await bazarr.GetAsync($"/api/v3/history/movie?movieId={version.Id}&eventType=grabbed")).ReadJsonAsync();
        Assert.Single(perMovie.EnumerateArray());

        // Media covers: the fixture film has no artwork, so the relative URL answers 404 rather than an HTML page.
        Assert.Empty(row.GetProperty("images").EnumerateArray());
        var cover = await bazarr.GetAsync($"/MediaCover/{version.Id}/poster-500.jpg");
        Assert.Equal(HttpStatusCode.NotFound, cover.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bazarr.GetAsync($"/api/v3/MediaCover/{version.Id}/fanart.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateAnonymousClient().GetAsync($"/MediaCover/{version.Id}/poster.jpg")).StatusCode);
        Assert.Equal("https://image.tmdb.org/t/p/w500/abc.jpg", MediaCoverEndpoints.Sized("https://image.tmdb.org/t/p/original/abc.jpg", ImageType.Poster, 500));
        Assert.Equal("https://image.tmdb.org/t/p/w780/abc.jpg", MediaCoverEndpoints.Sized("https://image.tmdb.org/t/p/original/abc.jpg", ImageType.Fanart, 360));
    }

    [Fact]
    public async Task The_signalr_hub_greets_with_a_version_and_routes_changes_by_view()
    {
        var session = await SessionAsync(factory);
        var (hd, uhd, root) = await SetupAsync(session);
        var (_, allKey) = await KeyClientAsync(factory, session, "bazarr-hub", "all");
        var (_, fourKKey) = await KeyClientAsync(factory, session, "seerr-hub-4k", "version:4k");

        await using var all = Connect(allKey, out var allMessages);
        await using var fourK = Connect(fourKKey, out var fourKMessages);
        await all.StartAsync();
        await fourK.StartAsync();

        var greeting = await WaitAsync(allMessages, m => m.GetProperty("name").GetString() == "version");
        Assert.Equal(RadarrCompat.ApplicationVersion, greeting.GetProperty("body").GetProperty("version").GetString());
        await WaitAsync(fourKMessages, m => m.GetProperty("name").GetString() == "version");

        // A 1080p default version: the all key hears "created", the 4K key hears nothing.
        var movie = await AddMovieAsync(session, 78, hd, root.Id);
        var created = await WaitAsync(allMessages, m => m.GetProperty("name").GetString() == "movie");
        Assert.Equal("created", created.GetProperty("body").GetProperty("action").GetString());
        Assert.Equal(movie.Versions.Single().Id, created.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32());
        Assert.Equal("Blade Runner", created.GetProperty("body").GetProperty("resource").GetProperty("title").GetString());
        Assert.Equal(1982, created.GetProperty("body").GetProperty("resource").GetProperty("year").GetInt32());

        // A 4K version: both hear it, with the 4K line's id.
        var fourKVersion = await AddVersionAsync(session, movie.Id, new Features.Movies.AddVersionRequest("4k", VersionMode.Managed, uhd));
        var heardByAll = await WaitAsync(allMessages, m => m.GetProperty("name").GetString() == "movie" && m.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32() == fourKVersion.Id);
        Assert.Equal("created", heardByAll.GetProperty("body").GetProperty("action").GetString());
        var heardBy4k = await WaitAsync(fourKMessages, m => m.GetProperty("name").GetString() == "movie");
        Assert.Equal(fourKVersion.Id, heardBy4k.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32());
        Assert.DoesNotContain(fourKMessages, m => m.GetProperty("name").GetString() == "movie" && m.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32() == movie.Versions.Single().Id);

        // Deleting the film: one "deleted" per version for the all key.
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/movies/{movie.Id}")).StatusCode);
        await WaitAsync(allMessages, m => m.GetProperty("name").GetString() == "movie" && m.GetProperty("body").GetProperty("action").GetString() == "deleted" && m.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32() == fourKVersion.Id);
        await WaitAsync(allMessages, m => m.GetProperty("name").GetString() == "movie" && m.GetProperty("body").GetProperty("action").GetString() == "deleted" && m.GetProperty("body").GetProperty("resource").GetProperty("id").GetInt32() == movie.Versions.Single().Id);
        await WaitAsync(fourKMessages, m => m.GetProperty("name").GetString() == "movie" && m.GetProperty("body").GetProperty("action").GetString() == "deleted");

        // Without a key the negotiation is refused.
        var anonymous = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, CompatHub.Route), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
    }

    [Fact]
    public void Routing_of_events_to_view_groups_is_pure()
    {
        var movie = new MovieRef(1, 603, null, "The Matrix", 1999);
        var hd = new VersionRef(10, "1080p", VersionMode.Managed, true, true, "1080p");
        var fourK = new VersionRef(11, "4k", VersionMode.Managed, false, true, "4K");

        var imported = Route(new FileImported(movie, hd, new FileRef(3, "/x", "Bluray-1080p", 1), null, null, false, "Hardlinked"));
        Assert.Equal(["view:1080p:version", "view:all", "view:default"], imported.Select(m => m.Group.Replace("view:version:1080p", "view:1080p:version")).Order());
        Assert.All(imported, m => Assert.Equal("updated", ((ResourceChange)m.Message.Body).Action));

        var updated = Route(new VersionUpdated(movie, hd));
        Assert.Equal(3, updated.Count);
        Assert.All(updated, m => Assert.Equal("updated", ((ResourceChange)m.Message.Body).Action));

        var renamed = Route(new FileRenamed(movie, fourK, new FileRef(4, "/y2", "Remux-2160p", 1), "/y"));
        Assert.Equal(["view:all", "view:version:4k"], renamed.Select(m => m.Group).Order());
        Assert.All(renamed, m => Assert.Equal("updated", ((ResourceChange)m.Message.Body).Action));

        var upgraded4k = Route(new FileUpgraded(movie, fourK, new FileRef(4, "/y", "Remux-2160p", 1), new FileRef(3, "/x", "Bluray-2160p", 1), null, null, false, "Copied"));
        Assert.DoesNotContain(upgraded4k, m => m.Group == "view:default");
        Assert.Contains(upgraded4k, m => m.Group == "view:version:4k");

        var switched = Route(new DefaultVersionChanged(movie, fourK, hd));
        Assert.Equal(["deleted", "updated"], switched.Select(m => ((ResourceChange)m.Message.Body).Action));
        Assert.All(switched, m => Assert.Equal("view:default", m.Group));

        var deleted = Route(new MovieDeleted(movie, true, [hd, fourK]));
        Assert.Equal(2, deleted.Count(m => m.Group == "view:all"));

        Assert.Empty(Route(new ReleaseGrabbed(movie, hd, new ReleaseRef("t", null, null, 0), 1, "qbt", "abc", "rss")));
    }

    private static List<(string Group, SignalRMessage Message)> Route(DomainEvent domainEvent) =>
        CompatHubBroadcaster.MessagesFor(new ServerEvent(1, domainEvent.Type, EventJson.Serialize(domainEvent), DateTime.UtcNow));

    private HubConnection Connect(string key, out ConcurrentQueue<JsonElement> messages)
    {
        var received = new ConcurrentQueue<JsonElement>();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, $"{CompatHub.Route}?access_token={Uri.EscapeDataString(key)}"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connection.On<JsonElement>(CompatHub.Method, message => received.Enqueue(message));
        messages = received;
        return connection;
    }

    private static async Task<JsonElement> WaitAsync(ConcurrentQueue<JsonElement> messages, Func<JsonElement, bool> match)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var hit = messages.FirstOrDefault(match);
            if (hit.ValueKind != JsonValueKind.Undefined)
            {
                return hit;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No hub message matched. Received: {string.Join(" | ", messages.Select(m => m.GetRawText()))}");
            }

            await Task.Delay(100);
        }
    }
}
