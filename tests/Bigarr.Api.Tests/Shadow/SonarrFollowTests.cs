using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bigarr.Api.Features.Shadow;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Migration;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Shadow;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>Shadow mode with the clock fixed on 2026-11-09 noon UTC, one app per test class.</summary>
public sealed class SonarrShadowApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now));

/// <summary>
/// Following a Sonarr in shadow mode, pass after pass against <see cref="FakeSonarr"/>: series come in through the import's
/// mapping, the history is read from the checkpoint (which only moves on success), a season pack grab is one row with
/// its seven episodes judged by Bigarr, a series added to Sonarr is searched on add with the whole-series rule (one request
/// by ids, then season by season), files and monitoring follow Sonarr's, the webhook wakes a pass, Compare pairs both sides,
/// and nothing but GET ever reaches Sonarr.
/// </summary>
public sealed class SonarrFollowTests(SonarrShadowApiFactory factory) : IClassFixture<SonarrShadowApiFactory>
{
    private static readonly DateTime Now = SeriesEndpointsTests.Now.UtcDateTime;

    [Fact]
    public async Task The_follow_records_sonarr_grabs_searches_new_series_and_keeps_the_library_in_line()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await SonarrImportTests.SeriesProfilesAsync(client);
        var tv = await SonarrImportTests.SeriesRootAsync(client, "bigarr-follow-tv-");
        var anime = await SonarrImportTests.SeriesRootAsync(client, "bigarr-follow-anime-");
        await AddMockIndexerAsync(client, "Mock follow");
        await using var sonarr = await FakeSonarr.StartAsync();

        // Frieren is in Sonarr from before the follow, with episode 1 on disk.
        var frieren = FakeSonarr.Frieren();
        var first = FakeSonarr.AddFile(frieren, new(21, 1, "Season 01/[SubsPlease] Sousou no Frieren - 01 (1080p).mkv", 3, "WEBDL-1080p", 1024, "SubsPlease"), (1, 1));
        FakeSonarr.Write(anime.Path, frieren, first);
        sonarr.Series.Add(frieren);

        var created = await client.PostAsJsonAsync("/api/v1/shadow/sonarrs", new FollowedSonarrRequest(
            "Sonarr 1080p", sonarr.Url, FakeSonarr.Key, "1080p", new() { [1] = hd }, new() { [FakeSonarr.RootPath] = tv.Id, [FakeSonarr.AnimeRootPath] = anime.Id }), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain(FakeSonarr.Key, body, StringComparison.Ordinal);
        var follow = JsonSerializer.Deserialize<FollowedSonarrResponse>(body, Json)!;
        Assert.Equal(FollowedSonarrService.KeyMask, follow.ApiKey);
        Assert.DoesNotContain(FakeSonarr.Key, await client.GetStringAsync("/api/v1/shadow"), StringComparison.Ordinal);
        var test = await client.PostAsync($"/api/v1/shadow/sonarrs/{follow.Id}/test", null);
        Assert.Equal(1, (await test.Content.ReadFromJsonAsync<FollowedSonarrTestResponse>(Json))!.Series);

        // Pass 1: Frieren comes in with its file; it was in Sonarr before the follow, so it is not searched.
        var pass1 = await SyncAsync(client, follow.Id);
        Assert.Contains("1 series added", pass1.Summary, StringComparison.Ordinal);
        Assert.Equal(0, pass1.SearchedOnAdd);
        var since = sonarr.Requests.First(r => r.Path == "/api/v3/history/since");
        var asked = DateTime.Parse(QueryHelpers.ParseQuery(since.Query)["date"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        Assert.Equal(follow.HistoryCheckpoint - SonarrFollower.Overlap, asked, TimeSpan.FromSeconds(1));
        Assert.Equal("true", QueryHelpers.ParseQuery(since.Query)["includeEpisode"].ToString());
        Assert.Equal(1, await CountAsync(db => db.EpisodeFiles.CountAsync()));

        // Sonarr is given Breaking Bad (Seerr), grabs its season 1 pack, and a single Frieren episode.
        var bb = FakeSonarr.BreakingBad(3);
        var added = new FakeSonarr.Show { Id = bb.Id, Title = bb.Title, Year = bb.Year, TvdbId = bb.TvdbId, Added = Now.AddMinutes(1) };
        foreach (var (season, monitored) in bb.Seasons)
        {
            added.Seasons[season] = monitored;
        }

        added.Episodes.AddRange(bb.Episodes);
        sonarr.Series.Add(added);
        var pack = "Breaking.Bad.S01.1080p.BluRay.x264-DEMAND";
        var grabData = new Dictionary<string, string>
        {
            ["indexer"] = "Mock follow",
            ["releaseGroup"] = "DEMAND",
            ["size"] = "20937965568",
            ["guid"] = "https://indexer.invalid/details/pack",
            ["torrentInfoHash"] = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            ["releaseSource"] = "Search",
            ["releaseType"] = "SeasonPack",
            ["customFormatScore"] = "10",
            ["downloadClientName"] = "qBittorrent",
        };
        for (var n = 1; n <= 7; n++)
        {
            sonarr.History.Add(new(200 + n, added.Id, sonarr.Episode(added, 1, n).Id, "grabbed", Now.AddMinutes(2), pack, 7, "Bluray-1080p", "ABCDEF0123456789ABCDEF0123456789ABCDEF01", grabData));
        }

        sonarr.History.Add(new(300, frieren.Id, sonarr.Episode(frieren, 1, 5).Id, "grabbed", Now.AddMinutes(3), "[SubsPlease] Sousou no Frieren - 05 (1080p) [ABCD1234].mkv", 3, "WEBDL-1080p", "FRIEREN05",
            new() { ["indexer"] = "Mock follow", ["releaseType"] = "SingleEpisode" }));

        // Pass 2, woken by Sonarr's webhook (the pass runs in the background a few seconds later).
        var hook = await client.PostAsync($"/api/v1/shadow/sonarrs/{follow.Id}/webhook", new StringContent("""{"eventType":"SeriesAdd"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, hook.StatusCode);
        await WaitUntilAsync(async () => (await GetAsync(client, follow.Id)).LastSummary?.Contains("series searched on add", StringComparison.Ordinal) == true ? (object)true : null, "the webhook pass", TimeSpan.FromSeconds(60));
        Assert.Equal("SeriesAdd", (await GetAsync(client, follow.Id)).LastWebhookEvent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            var grab = await db.SonarrGrabs.AsNoTracking().Include(g => g.Episodes).SingleAsync(g => g.TvdbId == 81189);
            Assert.Equal(7, grab.Episodes.Count);
            Assert.True(grab.FullSeason);
            Assert.Equal("SeasonPack", grab.ReleaseType);
            Assert.Equal("abcdef0123456789abcdef0123456789abcdef01", grab.InfoHash);
            Assert.Equal(10, grab.CustomFormatScore);
            Assert.Equal("Bluray-1080p", grab.Quality);
            Assert.NotNull(grab.VersionId);
            Assert.All(grab.Episodes, e => Assert.NotNull(e.EpisodeId));
            Assert.NotNull(grab.BigarrVerdict);
            Assert.Null(grab.BigarrVerdict.Unmatched);
            Assert.True(grab.BigarrVerdict.Approved, string.Join(" | ", grab.BigarrVerdict.Rejections));
            Assert.Equal("S01E01–S01E07 (7 episodes)", grab.BigarrVerdict.Episodes);
            Assert.Equal(RadarrGrabState.Grabbed, grab.State);

            // Search on add: one request by ids found the season 1 pack, season 2 was then searched on its own.
            var picks = await db.SeriesShadowPicks.AsNoTracking().Include(p => p.Episodes).Where(p => p.TvdbId == 81189).ToListAsync();
            Assert.All(picks, p => Assert.Equal("search:added", p.Trigger));
            Assert.Contains(picks, p => p.ReleaseTitle == pack && p.FullSeason);
            Assert.Contains(picks, p => p.Episodes.All(e => e.SeasonNumber == 2));
            Assert.Empty(await db.Grabs.ToListAsync());
        }

        // Compare: the pack is the same release on both sides, Frieren's episode is Sonarr only, season 2 Bigarr only.
        var compare = (await client.GetFromJsonAsync<SeriesCompareResponse>("/api/v1/shadow/compare/series", Json))!;
        var same = compare.Rows.Single(r => r.Outcome == SeriesCompareOutcome.Same);
        Assert.Equal(81189, same.TvdbId);
        Assert.Equal("S01E01–S01E07 (7 episodes)", same.Episodes);
        Assert.Equal(7, same.EpisodeCount);
        Assert.Equal(1, same.PackSeason);
        Assert.Contains(compare.Rows, r => r.Outcome == SeriesCompareOutcome.SonarrOnly && r.TvdbId == 424536 && r.Sighting is null && r.PackSeason is null);
        Assert.Contains(compare.Rows, r => r.Outcome == SeriesCompareOutcome.BigarrOnly && r.Episodes.StartsWith("S02E01", StringComparison.Ordinal));
        Assert.Equal(1, compare.Summary.Same);
        // One flat list of rows, newest first, paged by row.
        Assert.Equal(compare.Rows.Count, compare.Total);
        Assert.True(compare.Total >= 3);
        Assert.Equal(compare.Rows.OrderByDescending(r => r.At).Select(r => r.Key), compare.Rows.Select(r => r.Key));
        var onePerPage = (await client.GetFromJsonAsync<SeriesCompareResponse>("/api/v1/shadow/compare/series?pageSize=1", Json))!;
        Assert.Single(onePerPage.Rows);
        Assert.Equal(compare.Total, onePerPage.Total);
        Assert.Equal(compare.Rows[0].Key, onePerPage.Rows[0].Key);
        var page = (await client.GetFromJsonAsync<SeriesCompareResponse>("/api/v1/shadow/compare/series?tvdbId=424536", Json))!;
        Assert.NotEmpty(page.Rows);
        Assert.All(page.Rows, r => Assert.Equal(424536, r.TvdbId));

        // Sonarr imports the pack; on Frieren, deletes episode 1's file and unmonitors episode 2.
        for (var n = 1; n <= 7; n++)
        {
            var file = FakeSonarr.AddFile(added, new(100 + n, 1, $"Season 01/Breaking.Bad.S01E{n:00}.1080p.BluRay.x264-DEMAND.mkv", 7, "Bluray-1080p", 2048 + n, "DEMAND"), (1, n));
            FakeSonarr.Write(tv.Path, added, file);
            sonarr.History.Add(new(400 + n, added.Id, sonarr.Episode(added, 1, n).Id, "downloadFolderImported", Now.AddMinutes(30), pack, 7, "Bluray-1080p", "ABCDEF0123456789ABCDEF0123456789ABCDEF01", new()));
        }

        frieren.Files.Clear();
        sonarr.Episode(frieren, 1, 1).FileId = 0;
        sonarr.Episode(frieren, 1, 2).Monitored = false;
        sonarr.History.Add(new(500, frieren.Id, sonarr.Episode(frieren, 1, 1).Id, "episodeFileDeleted", Now.AddMinutes(31), "[SubsPlease] Sousou no Frieren - 01 (1080p).mkv", 3, "WEBDL-1080p", null, new() { ["reason"] = "Manual" }));

        // Sonarr is down for a while: the pass fails and the checkpoint stays.
        sonarr.Down = true;
        var before = (await GetAsync(client, follow.Id)).HistoryCheckpoint;
        var failed = await client.PostAsync($"/api/v1/shadow/sonarrs/{follow.Id}/sync", null);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var afterFailure = await GetAsync(client, follow.Id);
        Assert.Equal(before, afterFailure.HistoryCheckpoint);
        Assert.NotNull(afterFailure.LastError);
        sonarr.Down = false;

        var pass3 = await SyncAsync(client, follow.Id);
        Assert.Contains("grab(s) imported or failed", pass3.Summary, StringComparison.Ordinal);
        Assert.True((await GetAsync(client, follow.Id)).HistoryCheckpoint >= Now.AddMinutes(31));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
            Assert.Equal(RadarrGrabState.Imported, (await db.SonarrGrabs.AsNoTracking().SingleAsync(g => g.TvdbId == 81189)).State);
            var bbFiles = await db.EpisodeFiles.AsNoTracking().Where(f => f.Version.Series.TvdbId == 81189).CountAsync();
            Assert.Equal(7, bbFiles);
            Assert.False(await db.EpisodeFiles.AnyAsync(f => f.Version.Series.TvdbId == 424536));
            var target = await db.EpisodeTargets.AsNoTracking().SingleAsync(t => t.Version.Series.TvdbId == 424536 && t.Episode.SeasonNumber == 1 && t.Episode.EpisodeNumber == 2);
            Assert.False(target.Monitored);
            Assert.True(await db.History.AnyAsync(h => h.TvdbId == 424536 && h.EventType == Bigarr.Domain.History.HistoryEventType.FileDeleted));
        }

        Assert.All(sonarr.Requests, r => Assert.Equal("GET", r.Method));
    }

    [Fact]
    public async Task Search_on_add_is_capped_per_pass()
    {
        var client = await ClientAsync(factory);
        var (hd, _) = await SonarrImportTests.SeriesProfilesAsync(client);
        var tv = await SonarrImportTests.SeriesRootAsync(client, "bigarr-follow-cap-");
        await using var sonarr = await FakeSonarr.StartAsync();
        var created = await client.PostAsJsonAsync("/api/v1/shadow/sonarrs", new FollowedSonarrRequest(
            "Sonarr cap", sonarr.Url, FakeSonarr.Key, "cap", new() { [1] = hd }, new() { [FakeSonarr.RootPath] = tv.Id }), Json);
        var follow = (await created.Content.ReadFromJsonAsync<FollowedSonarrResponse>(Json))!;

        // A list import: 27 shows at once, each with one episode that has not aired yet.
        for (var i = 1; i <= SonarrFollower.SearchesOnAddPerPass + 2; i++)
        {
            var show = new FakeSonarr.Show { Id = 900 + i, Title = $"List Show {i}", Year = 2027, TvdbId = 9_000_000 + i, Added = Now.AddMinutes(1) };
            show.Seasons[1] = true;
            show.Episodes.Add(new FakeSonarr.Ep { Id = 90_000 + i, Season = 1, Number = 1, Title = "Pilot", AirDate = new DateOnly(2027, 1, 1) });
            sonarr.Series.Add(show);
        }

        var pass = await SyncAsync(client, follow.Id);
        Assert.Equal(SonarrFollower.SearchesOnAddPerPass, pass.SearchedOnAdd);
        Assert.Contains($"{SonarrFollower.SearchesOnAddPerPass + 2} series added", pass.Summary, StringComparison.Ordinal);

        // The bound postpones, it does not drop: the next pass searches the two left over, and then there is nothing left.
        Assert.Equal(2, (await SyncAsync(client, follow.Id)).SearchedOnAdd);
        Assert.Equal(0, (await SyncAsync(client, follow.Id)).SearchedOnAdd);
    }

    [Fact]
    public void A_failed_pass_forgets_the_series_list_but_not_when_every_series_was_last_read()
    {
        var memory = new SonarrFollowMemory();
        var read = new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
        memory.Remember(7, new SonarrFollowMemory.Entry(read.AddHours(20), new() { [1] = 12 }, read));

        memory.ForgetList(7);

        var entry = memory.Get(7)!;
        Assert.Equal(DateTime.MinValue, entry.ListReadAt); // the next pass reads the whole list
        Assert.Equal(read, entry.EverySeriesReadAt); // and the day's clock keeps running
        memory.ForgetList(8); // nothing remembered: nothing to do
        Assert.Null(memory.Get(8));
    }

    private async Task<int> CountAsync(Func<BigarrDbContext, Task<int>> count)
    {
        using var scope = factory.Services.CreateScope();
        return await count(scope.ServiceProvider.GetRequiredService<BigarrDbContext>());
    }

    private static async Task<SonarrFollowPass> SyncAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/v1/shadow/sonarrs/{id}/sync", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SonarrFollowPass>(Json))!;
    }

    private static async Task<FollowedSonarrResponse> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<FollowedSonarrResponse>($"/api/v1/shadow/sonarrs/{id}", Json))!;
}
