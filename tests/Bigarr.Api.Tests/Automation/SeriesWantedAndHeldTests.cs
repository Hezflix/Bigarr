using System.Net;
using System.Net.Http.Json;
using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Features.History;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Series;
using Bigarr.Api.Features.Server.Health;
using Bigarr.Api.Infrastructure.Matching;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Downloads;
using Bigarr.Domain.History;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Automation;

public sealed class ShadowSeriesApiFactory()
    : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now));

/// <summary>
/// The series Wanted list (Sonarr's order, light rows), the missing/cutoff-unmet job's due groups and backoff, and the
/// releases held for review: what RSS matched on a show's alternative order only, listed, counted in health, grabbed
/// or dismissed by hand.
/// </summary>
public sealed class SeriesWantedAndHeldTests(SeriesApiFactory factory) : IClassFixture<SeriesApiFactory>
{
    private const string DvdGroup = "69f50757054263b7bc87e32a";

    [Fact]
    public async Task The_wanted_list_puts_the_most_recently_aired_first_and_the_job_respects_the_backoff()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        var version = series.Versions.Single();

        // Every aired regular episode (the specials are not monitored) but one another test of the class may be downloading.
        var missing = (await client.GetFromJsonAsync<SeriesWantedPageResponse>($"/api/v1/series/wanted?filter=missing&seriesId={series.Id}&pageSize=200", Json))!;
        var downloading = await ActiveGrabsAsync(version.Id);
        Assert.Equal(62 - downloading, missing.Total);
        Assert.Equal((5, 16), (missing.Items[0].SeasonNumber, missing.Items[0].EpisodeNumber)); // the finale aired last
        Assert.Equal((1, 1), (missing.Items[^1].SeasonNumber, missing.Items[^1].EpisodeNumber));
        Assert.All(missing.Items, i => Assert.Equal(WantedFilter.Missing, i.Kind));
        Assert.All(missing.Items, i => Assert.Null(i.NextSearchAt)); // never searched: due at the next pass
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/series/wanted?filter=nope")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var wanted = scope.ServiceProvider.GetRequiredService<SeriesWantedService>();
        var targets = await db.EpisodeTargets.Include(t => t.Episode).Where(t => t.VersionId == version.Id).ToListAsync();
        try
        {
            // Everything searched half an hour ago, once: the first backoff (an hour) has not elapsed, nothing is due.
            var now = SeriesEndpointsTests.Now.UtcDateTime;
            foreach (var target in targets)
            {
                target.LastSearchAt = now.AddMinutes(-30);
                target.SearchAttempts = 1;
            }

            await db.SaveChangesAsync();
            Assert.DoesNotContain(await wanted.DueAsync(10, CancellationToken.None), g => g.VersionId == version.Id);

            // Season 2 searched two hours ago (due again); S03E05 searched three times (four hours of backoff) two hours ago (not due).
            foreach (var target in targets.Where(t => t.Episode.SeasonNumber == 2))
            {
                target.LastSearchAt = now.AddHours(-2);
            }

            var e305 = targets.Single(t => t.Episode.SeasonNumber == 3 && t.Episode.EpisodeNumber == 5);
            e305.LastSearchAt = now.AddHours(-2);
            e305.SearchAttempts = 3;
            await db.SaveChangesAsync();

            var due = Assert.Single(await wanted.DueAsync(10, CancellationToken.None), g => g.VersionId == version.Id);
            var season2 = targets.Where(t => t.Episode.SeasonNumber == 2).OrderBy(t => t.Episode.EpisodeNumber).Select(t => t.EpisodeId).ToArray();
            Assert.Equal(season2, due.EpisodeIds.ToArray());

            var page = (await client.GetFromJsonAsync<SeriesWantedPageResponse>($"/api/v1/series/wanted?filter=missing&seriesId={series.Id}&pageSize=200", Json))!;
            var item = page.Items.Single(i => i.EpisodeId == e305.EpisodeId);
            Assert.Equal(3, item.SearchAttempts);
            Assert.Equal(now.AddHours(2), item.NextSearchAt); // 60 min doubled twice
        }
        finally
        {
            foreach (var target in targets)
            {
                target.LastSearchAt = null;
                target.SearchAttempts = 0;
            }

            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public void A_season_wanted_whole_twice_is_a_big_gap_worth_one_request_by_the_shows_ids()
    {
        var now = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
        var episodes = Enumerable.Range(1, 3).SelectMany(season => Enumerable.Range(1, 4).Select(n => new Episode
        {
            Id = (season * 10) + n,
            Title = $"{season}x{n}",
            SeasonNumber = season,
            EpisodeNumber = n,
            AirDateUtc = now.AddDays(-400 + (season * 50) + n),
        })).ToList();

        Assert.True(SeriesAutomaticSearchService.IsBigGap(episodes.Where(e => e.SeasonNumber <= 2).ToList(), episodes, now));
        Assert.False(SeriesAutomaticSearchService.IsBigGap(episodes.Where(e => e.SeasonNumber == 1 || e.Id == 21).ToList(), episodes, now));
        Assert.False(SeriesAutomaticSearchService.IsBigGap(episodes.Where(e => e.SeasonNumber == 3).ToList(), episodes, now));
    }

    [Fact]
    public async Task A_release_rss_matched_on_the_order_only_is_held_for_review_then_grabbed_or_dismissed()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);
        var id = Guid.NewGuid().ToString("N")[..8];
        var downloadClient = await client.PostAsJsonAsync("/api/v1/downloadclients", new DownloadClientRequest($"Mock held {id}", DownloadClientImplementation.Mock, Category: $"films-{id}", SeriesCategory: $"tv-{id}"), Json);
        Assert.Equal(HttpStatusCode.Created, downloadClient.StatusCode);
        var apply = await client.PostAsJsonAsync($"/api/v1/series/{series.Id}/orders/apply", new OrderChoiceRequest(EpisodeOrder.TmdbEpisodeGroup, DvdGroup), Json);
        Assert.True(apply.StatusCode == HttpStatusCode.OK, await apply.Content.ReadAsStringAsync());
        var e14 = await SeriesSetup.EpisodeAsync(client, series.Id, 5, 14); // DVD S06E06

        try
        {
            // Two releases named by the DVD order: TVDB has no season 6, the order maps them, nothing in them confirms it.
            await RssAsync(["Breaking.Bad.S06E06.1080p.WEB-DL.DDP5.1.H.264-NTb", "Breaking.Bad.S06E06.720p.HDTV.x264-KILLERS"]);

            var held = (await client.GetFromJsonAsync<List<HeldReleaseResponse>>($"/api/v1/series/held?seriesId={series.Id}", Json))!;
            Assert.Equal(2, held.Count);
            Assert.All(held, h =>
            {
                Assert.Equal("S05E14", h.Episodes);
                Assert.Equal([e14.Id], h.EpisodeIds);
                Assert.Equal(SeriesReleaseJudge.HeldMessage, h.Reason);
                Assert.Equal("rss", h.Source);
                Assert.Equal(series.Versions.Single().Id, h.VersionId);
            });
            Assert.Empty(await GrabsAsync());

            var health = (await (await client.PostAsync("/api/v1/health/check", null)).Content.ReadFromJsonAsync<HealthResponse>(Json))!;
            Assert.Contains(health.Issues, i => i.Source == "heldReleases" && i.Message.StartsWith("2 release(s) held for review", StringComparison.Ordinal));

            // Dismissed: off the list, and the next pass does not bring it back.
            var hdtv = held.Single(h => h.Title.Contains("720p", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/series/held/{hdtv.Id}/dismiss", null)).StatusCode);
            await RssAsync(["Breaking.Bad.S06E06.720p.HDTV.x264-KILLERS"]);
            var left = Assert.Single((await client.GetFromJsonAsync<List<HeldReleaseResponse>>($"/api/v1/series/held?seriesId={series.Id}", Json))!);
            Assert.Contains("1080p", left.Title, StringComparison.Ordinal);
            Assert.Equal(2, (await client.GetFromJsonAsync<List<HeldReleaseResponse>>($"/api/v1/series/held?seriesId={series.Id}&includeDismissed=true", Json))!.Count);

            // Grabbed by hand: through the version's grab path, journaled as such, numbered on the order.
            var grab = await client.PostAsync($"/api/v1/series/held/{left.Id}/grab", null);
            Assert.True(grab.StatusCode == HttpStatusCode.OK, await grab.Content.ReadAsStringAsync());
            Assert.Empty((await client.GetFromJsonAsync<List<HeldReleaseResponse>>($"/api/v1/series/held?seriesId={series.Id}", Json))!);
            var sent = Assert.Single(await GrabsAsync());
            Assert.Equal(left.Title, sent.ReleaseTitle);
            Assert.Equal(EpisodeNumbering.Order, sent.Numbering);
            var history = (await client.GetFromJsonAsync<List<HistoryEventResponse>>($"/api/v1/history?seriesId={series.Id}&eventType=Grabbed", Json))!;
            Assert.Equal("held", Assert.Single(history, h => h.SourceTitle == left.Title).Data["trigger"]);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/series/held/{left.Id}/grab", null)).StatusCode);
        }
        finally
        {
            await client.PostAsync($"/api/v1/series/{series.Id}/orders/undo", null);
        }
    }

    private async Task<int> ActiveGrabsAsync(int versionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.EpisodeTargets.CountAsync(t => t.VersionId == versionId && db.GrabTargets.Any(g => g.Active && g.TargetId == t.Id));
    }

    /// <summary>The series half of an RSS pass on a hand-made feed, as the RSS task runs it.</summary>
    private async Task RssAsync(IEnumerable<string> titles)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        var indexer = await db.Indexers.AsNoTracking().FirstAsync();
        var feed = titles.Select(title => (indexer, new ReleaseInfo
        {
            Guid = $"held://{title}",
            Title = title,
            IndexerId = indexer.Id,
            IndexerName = indexer.Name,
            Protocol = DownloadProtocol.Torrent,
            Size = 1_500_000_000,
            InfoHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(title))),
            Seeders = 40,
            PublishDate = SeriesEndpointsTests.Now,
            Categories = [5040],
        })).ToList();
        await scope.ServiceProvider.GetRequiredService<SeriesRssPass>().RunAsync(feed, SeriesEndpointsTests.Now.UtcDateTime, new HashSet<string>(), CancellationToken.None);
    }

    private async Task<List<Grab>> GrabsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BigarrDbContext>().Grabs.AsNoTracking().Where(g => g.ReleaseTitle.StartsWith("Breaking.Bad.S06E06")).ToListAsync();
    }
}

/// <summary>Shadow mode (§10) holds for the new paths: the missing/cutoff-unmet job records series picks and sends nothing.</summary>
public sealed class SeriesShadowAutomationTests(ShadowSeriesApiFactory factory) : IClassFixture<ShadowSeriesApiFactory>
{
    [Fact]
    public async Task The_missing_search_job_turns_its_series_grabs_into_picks()
    {
        var client = await ClientAsync(factory);
        var series = await SeriesSetup.AddAsync(client, SeriesSearchTests.BreakingBad);
        await SeriesSetup.MockIndexerAsync(client);

        var run = await client.PostAsync($"/api/v1/system/tasks/{CutoffUnmetSearchJob.JobName}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var outcome = (await run.Content.ReadFromJsonAsync<Bigarr.Api.Features.Server.Tasks.TaskRunResponse>(Json))!;
        Assert.True(outcome.Success, outcome.Error);
        Assert.Contains("series: 1 version(s), 62 episode(s) searched", outcome.Summary, StringComparison.Ordinal);
        Assert.Contains("picked in shadow mode", outcome.Summary, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Empty(await db.Grabs.ToListAsync());
        var picks = await db.SeriesShadowPicks.Include(p => p.Episodes).Where(p => p.SeriesTitle == series.Title).ToListAsync();
        Assert.NotEmpty(picks);
        Assert.All(picks, p => Assert.Equal("search:cutoffUnmet", p.Trigger));

        // Searched: each wanted pair carries the search, those a pick covers start their backoff over.
        var targets = await db.EpisodeTargets.Where(t => t.VersionId == series.Versions.Single().Id && t.Monitored).ToListAsync();
        Assert.All(targets, t => Assert.NotNull(t.LastSearchAt));
    }
}
