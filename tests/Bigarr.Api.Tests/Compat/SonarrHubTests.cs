using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Compat.Sonarr;
using Bigarr.Api.Features.Library;
using Bigarr.Api.Features.Quality;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Events;
using Bigarr.Domain.Library;
using Bigarr.Domain.Tv;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using static Bigarr.Api.Tests.Compat.CompatTestHelpers;

namespace Bigarr.Api.Tests.Compat;

/// <summary>Bazarr's live side of the Sonarr surface: series and episode events over SignalR, filtered by the key's view.</summary>
public sealed class SonarrHubTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    private const int Firefly = 78874;
    private const string Api = SonarrCompat.RoutePrefix;

    [Fact]
    public async Task Bazarr_hears_series_changes_of_its_view_with_episodes_changed_on_monitoring()
    {
        var session = await SessionAsync(factory);
        var root = await SeriesRootAsync(session);
        var profiles = (await session.GetFromJsonAsync<List<QualityProfileSummary>>("/api/v1/qualityprofiles", Json))!;
        var hd = profiles.Single(p => p.Name == QualitySeeder.SeriesHdProfileName).Id;
        var uhd = profiles.Single(p => p.Name == QualitySeeder.SeriesUhdProfileName).Id;
        foreach (var show in (await session.GetFromJsonAsync<List<SeriesSummary>>("/api/v1/series", Json))!.Where(s => s.TvdbId == Firefly))
        {
            await session.DeleteAsync($"/api/v1/series/{show.Id}");
        }

        var (seerrHd, hdKey) = await KeyClientAsync(factory, session, "bazarr-hub-hd", "version:1080p");
        var (seerr4k, uhdKey) = await KeyClientAsync(factory, session, "bazarr-hub-4k", "version:4k");
        await using var hdHub = Connect(hdKey, out var hdMessages);
        await using var uhdHub = Connect(uhdKey, out var uhdMessages);
        await hdHub.StartAsync();
        await uhdHub.StartAsync();

        var greeting = await WaitAsync(hdMessages, m => Name(m) == "version");
        Assert.Equal(SonarrCompat.ApplicationVersion, greeting.GetProperty("body").GetProperty("version").GetString());

        // A show added through the 1080p key: "series updated" for that key, with what Bazarr requires; nothing for the 4K key.
        var added = await seerrHd.PostAsJsonAsync($"{Api}/series", Add(hd, root.Path));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var hdId = (await added.ReadJsonAsync()).GetProperty("id").GetInt32();
        var created = await WaitAsync(hdMessages, m => Name(m) == "series" && Id(m) == hdId);
        var resource = created.GetProperty("body").GetProperty("resource");
        Assert.Equal("updated", created.GetProperty("body").GetProperty("action").GetString());
        Assert.Equal("Firefly", resource.GetProperty("title").GetString());
        Assert.Equal(2002, resource.GetProperty("year").GetInt32());

        // Re-requesting a season flips its monitoring: episodesChanged, so Bazarr reads the episodes again.
        var lookup = Assert.Single((await (await seerrHd.GetAsync($"{Api}/series/lookup?term=tvdb:{Firefly}")).ReadJsonAsync()).EnumerateArray());
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(lookup.GetRawText())!;
        body["seasons"] = new[] { new { seasonNumber = 0, monitored = true }, new { seasonNumber = 1, monitored = true } };
        Assert.Equal(HttpStatusCode.Accepted, (await seerrHd.PutAsJsonAsync($"{Api}/series", body)).StatusCode);
        await WaitAsync(hdMessages, m => Name(m) == "series" && Id(m) == hdId && m.GetProperty("body").GetProperty("resource").GetProperty("episodesChanged").GetBoolean());

        // The 4K version: heard by the 4K key only; its deletion is "series deleted" there, the 1080p key hears neither.
        var added4k = await seerr4k.PostAsJsonAsync($"{Api}/series", Add(uhd, root.Path));
        var uhdId = (await added4k.ReadJsonAsync()).GetProperty("id").GetInt32();
        await WaitAsync(uhdMessages, m => Name(m) == "series" && Id(m) == uhdId);
        Assert.Equal(HttpStatusCode.OK, (await seerr4k.DeleteAsync($"{Api}/series/{uhdId}?deleteFiles=true&addImportExclusion=false")).StatusCode);
        var deleted = await WaitAsync(uhdMessages, m => Name(m) == "series" && Id(m) == uhdId && m.GetProperty("body").GetProperty("action").GetString() == "deleted");
        Assert.Equal("Firefly", deleted.GetProperty("body").GetProperty("resource").GetProperty("title").GetString());
        Assert.DoesNotContain(hdMessages, m => Name(m) == "series" && Id(m) == uhdId);
        Assert.DoesNotContain(uhdMessages, m => Name(m) == "series" && Id(m) == hdId);
        Assert.DoesNotContain(hdMessages, m => Name(m) == "movie");

        // Without a key the negotiation is refused.
        var anonymous = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, SonarrHub.Route), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());
    }

    [Fact]
    public void Episode_file_events_become_one_episode_message_per_episode_routed_by_view()
    {
        var show = new SeriesRef(1, 81189, 1396, null, "Breaking Bad", 2008, SeriesType.Standard);
        var hd = new SeriesVersionRef(10, "1080p", true, true, "Series 1080p");
        var fourK = new SeriesVersionRef(11, "4k", false, true, "Series 4K");
        var episodes = new List<EpisodeRef> { new(100, 1, 1, "Pilot"), new(101, 1, 2, "Cat's in the Bag...") };
        var file = new FileRef(5, "/tv/Breaking Bad/Season 1/x.mkv", "Bluray-1080p", 1);

        var imported = Route(new EpisodeFileImported(show, hd, episodes, file, null, null, 7, false, "Hardlinked"));
        Assert.Equal(6, imported.Count); // two episodes × (all, version:1080p, default)
        Assert.Equal(["sonarr:view:all", "sonarr:view:default", "sonarr:view:version:1080p"], imported.Select(m => m.Group).Distinct().Order());
        var stub = (SonarrEpisodeStub)((SonarrChange)imported[0].Message.Body).Resource;
        Assert.Equal("episode", imported[0].Message.Name);
        Assert.Equal(10, stub.SeriesId);
        Assert.Equal("Breaking Bad", stub.Series.Title);
        Assert.Equal(2008, stub.Series.Year);
        Assert.Equal([100, 101], imported.Select(m => ((SonarrEpisodeStub)((SonarrChange)m.Message.Body).Resource).Id).Distinct().Order());

        var deleted4k = Route(new EpisodeFileDeleted(show, fourK, [episodes[0]], file, "upgrade"));
        Assert.Equal(["sonarr:view:all", "sonarr:view:version:4k"], deleted4k.Select(m => m.Group).Order());

        var renamed = Route(new EpisodeFileRenamed(show, hd, [new RenamedFileRef(file, "/old.mkv")]));
        Assert.All(renamed, m => Assert.True(((SonarrSeriesStub)((SonarrChange)m.Message.Body).Resource).EpisodesChanged));

        var switched = Route(new SeriesDefaultVersionChanged(show, fourK, hd));
        Assert.Equal(["deleted", "updated"], switched.Select(m => ((SonarrChange)m.Message.Body).Action));
        Assert.All(switched, m => Assert.Equal("sonarr:view:default", m.Group));

        var gone = Route(new SeriesDeleted(show, true, [hd, fourK]));
        Assert.Equal(2, gone.Count(m => m.Group == "sonarr:view:all"));
        Assert.All(gone, m => Assert.Equal("deleted", ((SonarrChange)m.Message.Body).Action));

        // Film events are the Radarr hub's business.
        Assert.Empty(Route(new VersionUpdated(new MovieRef(1, 603, null, "The Matrix", 1999), new VersionRef(3, "1080p", Bigarr.Domain.Movies.VersionMode.Managed, true, true, "1080p"))));
    }

    private static List<(string Group, SignalRMessage Message)> Route(DomainEvent domainEvent) =>
        SonarrHubBroadcaster.MessagesFor(new ServerEvent(1, domainEvent.Type, EventJson.Serialize(domainEvent), DateTime.UtcNow));

    private static object Add(int profileId, string rootFolderPath) => new
    {
        tvdbId = Firefly,
        title = "Firefly",
        qualityProfileId = profileId,
        seasons = new[] { new { seasonNumber = 0, monitored = false }, new { seasonNumber = 1, monitored = false } },
        tags = Array.Empty<int>(),
        seasonFolder = true,
        monitored = true,
        monitorNewItems = "all",
        rootFolderPath,
        seriesType = "standard",
        addOptions = new { ignoreEpisodesWithFiles = true, searchForMissingEpisodes = false },
    };

    private static string? Name(JsonElement message) => message.GetProperty("name").GetString();

    private static int Id(JsonElement message) =>
        message.GetProperty("body").TryGetProperty("resource", out var resource) ? resource.GetProperty("id").GetInt32() : 0;

    private HubConnection Connect(string key, out ConcurrentQueue<JsonElement> messages)
    {
        var received = new ConcurrentQueue<JsonElement>();
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, $"{SonarrHub.Route}?access_token={Uri.EscapeDataString(key)}"), o =>
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

    private static async Task<RootFolderResponse> SeriesRootAsync(HttpClient session)
    {
        var roots = (await session.GetFromJsonAsync<List<RootFolderResponse>>("/api/v1/rootfolders", Json))!.Where(r => r.Kind == MediaKind.Series).ToList();
        if (roots.Count > 0)
        {
            return roots[0];
        }

        var created = await session.PostAsJsonAsync("/api/v1/rootfolders", new AddRootFolderRequest(Directory.CreateTempSubdirectory("bigarr-sonarr-hub-").FullName, MediaKind.Series));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<RootFolderResponse>(Json))!;
    }
}
