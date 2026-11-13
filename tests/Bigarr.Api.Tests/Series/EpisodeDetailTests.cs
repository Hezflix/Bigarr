using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Playback;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Bigarr.Domain.History;
using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Playback;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Series;

/// <summary>What the episode panel reads: the episode's history whatever the version, its plays on the media servers, its scene numbers.</summary>
public sealed class EpisodeDetailTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    [Fact]
    public async Task An_episode_history_holds_the_lines_of_its_own_targets_only()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Mock episode detail", Bigarr.Domain.Downloads.DownloadClientImplementation.Mock, Category: "films-detail", SeriesCategory: "tv-detail"), Json);
        var downloadClient = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        try
        {
            var pilot = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 1);
            var second = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 2);
            var third = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 3);
            var result = await SeriesSearchTests.SearchAsync(client, series.Id, $"episodeId={third.Id}");
            var single = result.Releases.First(r => r.Episodes.Count == 1 && r.Episodes[0].Id == third.Id && r.Versions.Any(v => v.Approved));
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/grab", SeriesSetup.Grab(single), Json)).StatusCode);

            var lines = (await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?episodeId={third.Id}", Json))!;
            var grabbed = Assert.Single(lines);
            Assert.Equal(HistoryEventType.Grabbed, grabbed.EventType);
            Assert.Equal(single.Title, grabbed.SourceTitle);
            Assert.Equal(series.Id, grabbed.SeriesId);
            Assert.Equal("S01E03", grabbed.Data["episode"]);

            Assert.Empty((await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?episodeId={pilot.Id}", Json))!);
            Assert.Empty((await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?episodeId={second.Id}", Json))!);
            Assert.Empty((await client.GetFromJsonAsync<List<HistoryEventResponse>>("/api/v1/history?episodeId=999999", Json))!);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    [Fact]
    public async Task An_episode_plays_are_found_by_its_tvdb_id_or_by_the_show_and_its_numbers()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var pilot = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 1);
        var second = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 2);
        Assert.NotNull(pilot.TvdbId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var server = db.MediaServers.Add(new MediaServer { Name = "Demo server", Url = "http://127.0.0.1:9", Token = "demo" }).Entity;
            await db.SaveChangesAsync();
            var now = SeriesEndpointsTests.Now.UtcDateTime;
            var user = db.MediaServerUsers.Add(new MediaServerUser { MediaServerId = server.Id, ExternalId = "1", Name = "alice", FirstSeenAt = now, LastSeenAt = now }).Entity;
            // The pilot by its own TVDB id (numbers the server got wrong), the second episode by the show's id and its numbers,
            // and another show's 1x01 that must not count.
            var byId = Item(server, "1", pilot.TvdbId, null, 9, 9, "Pilot");
            var byNumbers = Item(server, "2", null, SeriesSearchTests.BreakingBad, 1, 2, "Cat's in the Bag...");
            var other = Item(server, "3", null, 999_999, 1, 1, "Someone else's pilot");
            db.MediaServerItems.AddRange(byId, byNumbers, other);
            db.Plays.AddRange(Play(user, byId, now.AddDays(-2), 2400), Play(user, byId, now.AddDays(-1), 600), Play(user, byNumbers, now.AddHours(-3), 2800), Play(user, other, now.AddHours(-2), 1200));
            await db.SaveChangesAsync();
        }

        var pilotPlays = (await client.GetFromJsonAsync<PlaysPageResponse>($"/api/v1/playback/plays?episodeId={pilot.Id}", Json))!;
        Assert.Equal(2, pilotPlays.TotalRecords);
        Assert.All(pilotPlays.Records, p => Assert.Equal("alice", p.User.Name));
        Assert.Equal([600, 2400], pilotPlays.Records.Select(p => p.PlayedSeconds));

        var secondPlays = (await client.GetFromJsonAsync<PlaysPageResponse>($"/api/v1/playback/plays?episodeId={second.Id}", Json))!;
        Assert.Equal(2800, Assert.Single(secondPlays.Records).PlayedSeconds);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/playback/plays?episodeId=999999")).StatusCode);
    }

    [Fact]
    public async Task An_episode_automatic_search_grabs_the_best_release_for_each_version()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var created = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest("Mock episode search", Bigarr.Domain.Downloads.DownloadClientImplementation.Mock, Category: "films-auto", SeriesCategory: "tv-auto"), Json);
        var downloadClient = (await created.Content.ReadFromJsonAsync<DownloadClientResponse>(Json))!;
        try
        {
            var fifth = await SeriesSetup.EpisodeAsync(client, series.Id, 1, 5);
            var response = await client.PostAsync($"/api/v1/series/{series.Id}/episodes/{fifth.Id}/search", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = Assert.Single((await response.Content.ReadFromJsonAsync<List<SeriesAutomaticSearchResult>>(Json))!);
            Assert.Equal(1, result.Wanted);
            Assert.Equal(1, result.Grabbed);

            var grabbed = Assert.Single((await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?episodeId={fifth.Id}", Json))!);
            Assert.Contains("S01E05", grabbed.SourceTitle, StringComparison.Ordinal);

            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/series/{series.Id}/episodes/999999/search", null)).StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/v1/downloadclients/{downloadClient.Id}");
        }
    }

    private static MediaServerItem Item(MediaServer server, string key, int? tvdbId, int? showTvdbId, int season, int episode, string title) => new()
    {
        MediaServerId = server.Id,
        RatingKey = key,
        Kind = MediaItemKind.Episode,
        Title = title,
        TvdbId = tvdbId,
        ShowTvdbId = showTvdbId,
        ShowTitle = "Breaking Bad",
        SeasonNumber = season,
        EpisodeNumber = episode,
        DurationMs = 48 * 60_000L,
        UpdatedAt = SeriesEndpointsTests.Now.UtcDateTime,
    };

    private static Play Play(MediaServerUser user, MediaServerItem item, DateTime start, int played) => new()
    {
        User = user,
        Item = item,
        StartedAt = start,
        StoppedAt = start.AddSeconds(played),
        PlayedSeconds = played,
        Watched = played > 2000,
    };
}
