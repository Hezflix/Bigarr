using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Infrastructure.MediaServers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Compat;
using Bigarr.Api.Tests.Hosting;
using Bigarr.Domain.Events;
using Bigarr.Domain.Library;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;
using Microsoft.Extensions.DependencyInjection;

namespace Bigarr.Api.Tests.MediaServers;

/// <summary>Media servers (M8): an imported file makes Plex and Jellyfin scan its folder, under their own paths; a server that fails shows in health.</summary>
public sealed class MediaServerTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    [Fact]
    public async Task Plex_and_jellyfin_scan_the_imported_folder_under_their_own_paths_and_a_failing_server_is_reported()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        await using var plex = await FakeHttpServer.StartAsync();
        plex.Responder = Plex;
        await using var jellyfin = await FakeHttpServer.StartAsync();
        jellyfin.Responder = Jellyfin;

        var mappings = new[] { new { from = "/data", to = "/mnt/media" } };
        var plexCreated = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Plex", kind = "plex", url = plex.Url, token = "plex-token", enabled = true, pathMappings = mappings });
        Assert.Equal(HttpStatusCode.Created, plexCreated.StatusCode);
        var plexId = (await plexCreated.ReadJsonAsync()).GetProperty("id").GetInt32();

        var jellyfinCreated = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Jellyfin", kind = "jellyfin", url = jellyfin.Url, token = "jf-key", enabled = true, pathMappings = mappings });
        Assert.Equal(HttpStatusCode.Created, jellyfinCreated.StatusCode);
        var jellyfinId = (await jellyfinCreated.ReadJsonAsync()).GetProperty("id").GetInt32();

        var tested = await session.PostAsync($"/api/v1/mediaservers/{plexId}/test", null);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var info = await tested.ReadJsonAsync();
        Assert.Equal("Fake Plex", info.GetProperty("name").GetString());
        Assert.Equal(1, info.GetProperty("movieLibraries").GetInt32());

        var jellyfinTested = await (await session.PostAsync($"/api/v1/mediaservers/{jellyfinId}/test", null)).ReadJsonAsync();
        Assert.Equal("Fake Jellyfin", jellyfinTested.GetProperty("name").GetString());

        var badToken = await session.PostAsJsonAsync("/api/v1/mediaservers/test", new { name = "x", kind = "plex", url = plex.Url, token = "wrong", enabled = true });
        Assert.Equal(HttpStatusCode.BadGateway, badToken.StatusCode);

        // A file lands: both servers hear about its folder, translated to /mnt/media.
        await PublishImportAsync("/data/movies/Heat (1995)/Heat.1995.1080p.BluRay.mkv");
        var plexRefresh = await plex.WaitForAsync(r => r.Path == "/library/sections/1/refresh" && r.Query.Contains("path=", StringComparison.Ordinal));
        Assert.Contains("/mnt/media/movies/Heat (1995)", Uri.UnescapeDataString(plexRefresh.Query), StringComparison.Ordinal);
        Assert.Equal("plex-token", plexRefresh.Headers["X-Plex-Token"]);
        Assert.DoesNotContain(plex.Requests, r => r.Path == "/library/sections/2/refresh");

        var jellyfinUpdate = await jellyfin.WaitForAsync(r => r.Path == "/Library/Media/Updated");
        using (var body = JsonDocument.Parse(jellyfinUpdate.Body))
        {
            var update = body.RootElement.GetProperty("Updates").EnumerateArray().Single();
            Assert.Equal("/mnt/media/movies/Heat (1995)", update.GetProperty("Path").GetString());
            Assert.Equal("Created", update.GetProperty("UpdateType").GetString());
        }

        Assert.Equal("jf-key", jellyfinUpdate.Headers["X-Emby-Token"]);

        // Refresh now: a full scan of the movie section, no path.
        var refreshed = await session.PostAsync($"/api/v1/mediaservers/{plexId}/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Contains(plex.Requests, r => r.Path == "/library/sections/1/refresh" && r.Query.Length == 0);
        Assert.Equal(JsonValueKind.Null, (await refreshed.ReadJsonAsync()).GetProperty("lastError").ValueKind);

        // Plex goes down: the failure lands on the row and in the health checks.
        plex.Responder = r => r.Path.StartsWith("/library/sections/", StringComparison.Ordinal) && r.Path.EndsWith("/refresh", StringComparison.Ordinal) ? new FakeHttpServer.Answer(500) : Plex(r);
        await PublishImportAsync("/data/movies/Se7en (1995)/Se7en.1995.mkv");
        var failing = await WaitUntilAsync(async () =>
        {
            var server = await (await session.GetAsync($"/api/v1/mediaservers/{plexId}")).ReadJsonAsync();
            return server.GetProperty("isFailing").GetBoolean() ? server : (JsonElement?)null;
        });
        Assert.Contains("HTTP 500", failing.GetProperty("lastError").GetString(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await session.PostAsync("/api/v1/health/check", null)).StatusCode);
        var health = await (await session.GetAsync("/api/v1/health")).Content.ReadAsStringAsync();
        Assert.Contains("mediaservers", health, StringComparison.Ordinal);
        Assert.Contains("Plex could not be refreshed", health, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/mediaservers/{plexId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/mediaservers/{jellyfinId}")).StatusCode);
    }

    [Fact]
    public async Task A_server_deleted_while_it_is_being_told_does_not_stop_later_refreshes()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        await using var slow = await FakeHttpServer.StartAsync();
        using var answer = new ManualResetEventSlim();
        slow.Responder = r =>
        {
            if (r.Path == "/Library/Media/Updated")
            {
                answer.Wait(TimeSpan.FromSeconds(20));
            }

            return Jellyfin(r);
        };
        var created = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Slow Jellyfin", kind = "jellyfin", url = slow.Url, token = "jf-key", enabled = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var slowId = (await created.ReadJsonAsync()).GetProperty("id").GetInt32();

        // The refresh is in flight when the server is deleted: its outcome has no row to land on.
        await PublishImportAsync("/data/movies/Ronin (1998)/Ronin.1998.mkv");
        await slow.WaitForAsync(r => r.Path == "/Library/Media/Updated");
        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/mediaservers/{slowId}")).StatusCode);
        answer.Set();

        // The worker is still there for the next server and the next file.
        await using var next = await FakeHttpServer.StartAsync();
        next.Responder = Jellyfin;
        var nextCreated = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Next Jellyfin", kind = "jellyfin", url = next.Url, token = "jf-key", enabled = true });
        var nextId = (await nextCreated.ReadJsonAsync()).GetProperty("id").GetInt32();
        await PublishImportAsync("/data/movies/Thief (1981)/Thief.1981.mkv");
        var update = await next.WaitForAsync(r => r.Path == "/Library/Media/Updated");
        Assert.Contains("Thief (1981)", update.Body, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await session.DeleteAsync($"/api/v1/mediaservers/{nextId}")).StatusCode);
    }

    [Fact]
    public void Paths_are_translated_by_prefix_and_only_file_events_ask_for_a_scan()
    {
        var server = new MediaServer { Name = "Plex", Url = "http://plex:32400", Token = "t", PathMappings = [new PathMapping("/data/", "/mnt/media"), new PathMapping("/other", "D:\\Media")] };
        Assert.Equal("/mnt/media/movies/Heat (1995)", server.Translate("/data/movies/Heat (1995)"));
        Assert.Equal("D:\\Media\\movies\\Heat", server.Translate("/other/movies/Heat"));
        Assert.Equal("/database/movies", server.Translate("/database/movies"));
        Assert.Equal("/elsewhere", server.Translate("/elsewhere"));

        var movie = new MovieRef(1, 949, null, "Heat", 1995);
        var version = new VersionRef(2, "1080p", VersionMode.Managed, true, true, "1080p", "/data/movies/Heat (1995)");

        var imported = MediaServerRefresher.ChangeOf(Event(new FileImported(movie, version, new FileRef(3, "/data/movies/Heat (1995)/Heat.mkv", "Bluray-1080p", 1), null, null, false, "Hardlinked")));
        Assert.Equal(("/data/movies/Heat (1995)", MediaChange.Created), imported);

        var deleted = MediaServerRefresher.ChangeOf(Event(new VersionDeleted(movie, version, true)));
        Assert.Equal(("/data/movies/Heat (1995)", MediaChange.Deleted), deleted);

        Assert.Null(MediaServerRefresher.ChangeOf(Event(new VersionDeleted(movie, version, false))));
        Assert.Null(MediaServerRefresher.ChangeOf(Event(new ReleaseGrabbed(movie, version, new ReleaseRef("t", null, null, 0), 1, "qbt", "abc", "rss"))));
        Assert.Null(MediaServerRefresher.ChangeOf(Event(new VersionUpdated(movie, version))));
    }

    [Fact]
    public void Series_events_ask_for_the_season_folder_of_each_file_and_the_versions_folder_when_it_goes()
    {
        var show = new SeriesRef(1, 81189, 1396, null, "Breaking Bad", 2008, Bigarr.Domain.Tv.SeriesType.Standard);
        var version = new SeriesVersionRef(2, "1080p", true, true, "HD", "/data/tv/Breaking Bad (2008)");
        var pilot = new EpisodeRef(3, 1, 1, "Pilot");
        var file = new FileRef(4, "/data/tv/Breaking Bad (2008)/Season 1/Breaking Bad - S01E01.mkv", "Bluray-1080p", 1);

        var imported = Assert.Single(MediaServerRefresher.ChangesOf(Event(new EpisodeFileImported(show, version, [pilot], file, null, null, null, false, "Hardlinked"))));
        Assert.Equal(new MediaServerRefresher.FolderChange("/data/tv/Breaking Bad (2008)/Season 1", MediaChange.Created, MediaKind.Series), imported);
        var upgraded = Assert.Single(MediaServerRefresher.ChangesOf(Event(new EpisodeFileImported(show, version, [pilot], file, [file with { Id = 5 }], null, null, false, "Hardlinked"))));
        Assert.Equal(MediaChange.Modified, upgraded.Change);

        // A rename can move a file to another season folder: both folders are scanned.
        var renamed = MediaServerRefresher.ChangesOf(Event(new EpisodeFileRenamed(show, version, [new RenamedFileRef(file, "/data/tv/Breaking Bad (2008)/Season 01/old.mkv")])));
        Assert.Equal(["/data/tv/Breaking Bad (2008)/Season 01", "/data/tv/Breaking Bad (2008)/Season 1"], renamed.Select(c => c.Folder).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(MediaChange.Deleted, Assert.Single(MediaServerRefresher.ChangesOf(Event(new EpisodeFileDeleted(show, version, [pilot], file, "manual")))).Change);
        Assert.Equal("/data/tv/Breaking Bad (2008)", Assert.Single(MediaServerRefresher.ChangesOf(Event(new SeriesDeleted(show, true, [version])))).Folder);
        Assert.Empty(MediaServerRefresher.ChangesOf(Event(new SeriesDeleted(show, false, [version]))));
        Assert.Equal("/data/tv/Breaking Bad (2008)", Assert.Single(MediaServerRefresher.ChangesOf(Event(new SeriesVersionDeleted(show, version, true)))).Folder);
        Assert.Empty(MediaServerRefresher.ChangesOf(Event(new EpisodeGrabbed(show, version, [pilot], new ReleaseRef("t", null, null, 0), 1, "qbt", "abc", "rss"))));
    }

    [Fact]
    public async Task An_episode_import_makes_plex_scan_its_tv_section_under_its_own_path()
    {
        var session = await CompatTestHelpers.SessionAsync(factory);
        await using var plex = await FakeHttpServer.StartAsync();
        plex.Responder = Plex;
        var created = await session.PostAsJsonAsync("/api/v1/mediaservers", new { name = "Plex TV", kind = "plex", url = plex.Url, token = "plex-token", enabled = true, pathMappings = new[] { new { from = "/data", to = "/mnt/media" } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var show = new SeriesRef(1, 81189, 1396, null, "Breaking Bad", 2008, Bigarr.Domain.Tv.SeriesType.Standard);
            var version = new SeriesVersionRef(2, "1080p", true, true, "HD", "/data/tv/Breaking Bad (2008)");
            scope.ServiceProvider.GetRequiredService<OutboxWriter>().Publish(new EpisodeFileImported(
                show, version, [new EpisodeRef(3, 1, 1, "Pilot")], new FileRef(4, "/data/tv/Breaking Bad (2008)/Season 1/Breaking Bad - S01E01.mkv", "Bluray-1080p", 1), null, null, null, false, "Hardlinked"));
            await db.SaveChangesAsync();
        }

        // The TV section (2) scans the season folder, translated; the movie section is left alone.
        var refresh = await plex.WaitForAsync(r => r.Path == "/library/sections/2/refresh" && r.Query.Contains("path=", StringComparison.Ordinal));
        Assert.Contains(Uri.EscapeDataString("/mnt/media/tv/Breaking Bad (2008)/Season 1"), refresh.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(plex.Requests, r => r.Path == "/library/sections/1/refresh");
    }

    private static ServerEvent Event(DomainEvent domainEvent) => new(1, domainEvent.Type, EventJson.Serialize(domainEvent), DateTime.UtcNow);

    private async Task PublishImportAsync(string filePath)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<OutboxWriter>();
        var movie = new MovieRef(1, 949, null, "Heat", 1995);
        var version = new VersionRef(2, "1080p", VersionMode.Managed, true, true, "1080p", Path.GetDirectoryName(filePath));
        outbox.Publish(new FileImported(movie, version, new FileRef(3, filePath, "Bluray-1080p", 1), null, null, false, "Hardlinked"));
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> WaitUntilAsync(Func<Task<JsonElement?>> probe)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition never held.");
            }

            await Task.Delay(100);
        }
    }

    private static FakeHttpServer.Answer? Plex(FakeHttpServer.Received request)
    {
        if (!request.Headers.TryGetValue("X-Plex-Token", out var token) || token != "plex-token")
        {
            return new FakeHttpServer.Answer(401);
        }

        return request.Path switch
        {
            "/" => new FakeHttpServer.Answer(200, """{"MediaContainer":{"friendlyName":"Fake Plex","version":"1.41.0.8994"}}"""),
            "/library/sections" => new FakeHttpServer.Answer(200, """{"MediaContainer":{"size":2,"Directory":[{"key":"1","type":"movie","title":"Movies","Location":[{"id":1,"path":"/mnt/media/movies"}]},{"key":"2","type":"show","title":"TV","Location":[{"id":2,"path":"/mnt/media/tv"}]}]}}"""),
            "/library/sections/1/refresh" or "/library/sections/2/refresh" => new FakeHttpServer.Answer(200),
            _ => null,
        };
    }

    private static FakeHttpServer.Answer? Jellyfin(FakeHttpServer.Received request)
    {
        if (!request.Headers.TryGetValue("X-Emby-Token", out var token) || token != "jf-key")
        {
            return new FakeHttpServer.Answer(401);
        }

        return (request.Method, request.Path) switch
        {
            ("GET", "/System/Info") => new FakeHttpServer.Answer(200, """{"ServerName":"Fake Jellyfin","Version":"10.10.3","Id":"abc"}"""),
            ("GET", "/Library/VirtualFolders") => new FakeHttpServer.Answer(200, """[{"Name":"Movies","CollectionType":"movies"},{"Name":"Shows","CollectionType":"tvshows"}]"""),
            ("POST", "/Library/Media/Updated") => new FakeHttpServer.Answer(204),
            ("POST", "/Library/Refresh") => new FakeHttpServer.Answer(204),
            _ => null,
        };
    }
}
