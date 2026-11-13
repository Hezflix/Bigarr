using System.Globalization;
using System.Net.Http.Json;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Features.Server.Tasks;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// A fresh torrent often shows up with no seeders yet. When a version would take it but for that, the pass plans one
/// quick recheck of the feed that carried it, a few minutes later, instead of leaving it to the next pass.
/// </summary>
public sealed class RssQuickRecheckTests(RssSightingsApiFactory factory) : IClassFixture<RssSightingsApiFactory>
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A_film_release_waiting_for_seeders_is_rechecked_on_its_indexer_only_and_picked()
    {
        var client = await FreshAsync();
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Directory.CreateTempSubdirectory("bigarr-rss-recheck-").FullName);
        await AddMovieAsync(client, 603, hd, root.Id);
        var waitingOn = await AddMockIndexerAsync(client, "Scripted recheck film");
        var other = await AddMockIndexerAsync(client, "Scripted recheck other");
        var title = "The.Matrix.1999.1080p.BluRay.x264-GRP";
        var seeders = 0;
        var calls = new Calls();
        factory.Feeds.Films = indexer => calls.Film(indexer, indexer.Id == waitingOn.Id ? [Release(waitingOn, "recheck-film/1", title, null, seeders)] : []);
        factory.Feeds.Series = indexer => calls.Series(indexer, []);

        var first = await RunAsync(client);

        Assert.Equal(0, first.Picked);
        var plan = Assert.IsType<RssRecheckPlan>(State.PendingRecheck);
        Assert.Equal([waitingOn.Id], plan.Indexers.Keys);
        Assert.Equal(new RssRecheckFeeds(Films: true, Series: false), plan.Indexers[waitingOn.Id]);
        Assert.Equal(1, plan.Waiting);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime + Delay, Scheduler.NextRunAt(RssRecheckJob.JobName));

        factory.Clock.Advance(Delay);
        seeders = 3;
        calls.Clear();
        var recheck = await RecheckAsync(client);

        Assert.Equal(1, calls.FilmCount(waitingOn.Id));
        Assert.Equal(0, calls.SeriesCount(waitingOn.Id));
        Assert.Equal(0, calls.FilmCount(other.Id) + calls.SeriesCount(other.Id));
        Assert.Contains("1 release(s) waiting for seeders", recheck.Summary, StringComparison.Ordinal);
        Assert.Contains("1 picked", recheck.Summary, StringComparison.Ordinal);
        Assert.True(await FilmPickedAsync(title));
        Assert.True(Assert.Single((await SightingAsync("recheck-film/1")).Verdicts).Approved);
        Assert.Null(State.PendingRecheck);
        Assert.Null(Scheduler.NextRunAt(RssRecheckJob.JobName));
    }

    [Fact]
    public async Task An_episode_release_waiting_for_seeders_is_rechecked_on_the_series_feed_and_picked()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck series");
        var title = "Breaking.Bad.S05E06.1080p.WEB-DL.DD5.1.H.264-BS";
        var seeders = 0;
        var calls = new Calls();
        factory.Feeds.Films = i => calls.Film(i, []);
        factory.Feeds.Series = i => calls.Series(i, [Release(indexer, "recheck-series/1", title, 81189, seeders)]);

        Assert.Equal(0, (await RunAsync(client)).Picked);
        var plan = Assert.IsType<RssRecheckPlan>(State.PendingRecheck);
        Assert.Equal(new RssRecheckFeeds(Films: false, Series: true), plan.Indexers[indexer.Id]);

        factory.Clock.Advance(Delay);
        seeders = 3;
        calls.Clear();
        var recheck = await RecheckAsync(client);

        Assert.Equal(0, calls.FilmCount(indexer.Id));
        Assert.Equal(1, calls.SeriesCount(indexer.Id));
        Assert.Contains("1 picked", recheck.Summary, StringComparison.Ordinal);
        Assert.True(await EpisodePickedAsync(title));
        Assert.Contains((await SightingAsync("recheck-series/1")).Verdicts, v => v.Approved);
    }

    [Fact]
    public async Task No_recheck_when_a_lasting_reason_turned_the_release_down_too()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck lasting");
        // 2160p is not in the HD profile, whatever the seeders say.
        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "recheck-lasting/1", "Breaking.Bad.S05E07.2160p.WEB-DL.DD5.1.H.264-BS", 81189, 0)];

        await RunAsync(client);

        Assert.Null(State.PendingRecheck);
        Assert.Null(Scheduler.NextRunAt(RssRecheckJob.JobName));
    }

    [Fact]
    public async Task No_recheck_when_nothing_of_the_library_wants_the_release()
    {
        var client = await FreshAsync();
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck unwanted");
        factory.Feeds.Films = _ => [Release(indexer, "recheck-unwanted/1", "Some.Unknown.Film.2017.1080p.BluRay.x264-GRP", null, 0)];
        factory.Feeds.Series = _ => [Release(indexer, "recheck-unwanted/2", "Some.Unknown.Show.S01E01.1080p.WEB.h264-GRP", null, 0)];

        await RunAsync(client);

        Assert.Null(State.PendingRecheck);
    }

    [Fact]
    public async Task A_recheck_that_still_finds_no_seeders_does_not_plan_another()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck chain");
        var calls = new Calls();
        factory.Feeds.Films = i => calls.Film(i, []);
        factory.Feeds.Series = i => calls.Series(i, [Release(indexer, "recheck-chain/1", "Breaking.Bad.S05E08.1080p.WEB-DL.DD5.1.H.264-BS", 81189, 0)]);

        await RunAsync(client);
        Assert.NotNull(State.PendingRecheck);

        factory.Clock.Advance(Delay);
        var recheck = await RecheckAsync(client);
        Assert.Contains("waiting for seeders", recheck.Summary, StringComparison.Ordinal);
        Assert.Null(State.PendingRecheck);
        Assert.Null(Scheduler.NextRunAt(RssRecheckJob.JobName));

        // Run again by hand: nothing planned, nothing read.
        calls.Clear();
        Assert.Equal("Nothing to recheck", (await RecheckAsync(client)).Summary);
        Assert.Equal(0, calls.SeriesCount(indexer.Id) + calls.FilmCount(indexer.Id));
    }

    [Fact]
    public async Task A_delay_of_zero_turns_the_recheck_off()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck off");
        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "recheck-off/1", "Breaking.Bad.S05E09.1080p.WEB-DL.DD5.1.H.264-BS", 81189, 0)];
        var options = factory.Services.GetRequiredService<IOptions<RssSyncOptions>>().Value;
        options.QuickRecheckMinutes = 0;
        try
        {
            await RunAsync(client);

            Assert.Null(State.PendingRecheck);
            Assert.Null(Scheduler.NextRunAt(RssRecheckJob.JobName));
            var task = (await client.GetFromJsonAsync<List<ScheduledTaskResponse>>("/api/v1/system/tasks", Json))!.Single(t => t.Name == RssRecheckJob.JobName);
            Assert.False(task.Enabled);
        }
        finally
        {
            options.QuickRecheckMinutes = 5;
        }
    }

    [Fact]
    public async Task A_whole_pass_before_the_recheck_takes_its_place()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck overtaken");
        var title = "Breaking.Bad.S05E10.1080p.WEB-DL.DD5.1.H.264-BS";
        var seeders = 0;
        var calls = new Calls();
        factory.Feeds.Films = i => calls.Film(i, []);
        factory.Feeds.Series = i => calls.Series(i, [Release(indexer, "recheck-overtaken/1", title, 81189, seeders)]);

        await RunAsync(client);
        Assert.NotNull(State.PendingRecheck);

        // A whole pass comes first (run by hand) and finds the seeders: it picks, and the recheck has nothing left to do.
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        seeders = 3;
        Assert.Equal(1, (await RunAsync(client)).Picked);
        Assert.Null(State.PendingRecheck);
        Assert.Null(Scheduler.NextRunAt(RssRecheckJob.JobName));

        factory.Clock.Advance(Delay);
        calls.Clear();
        Assert.Equal("Nothing to recheck", (await RecheckAsync(client)).Summary);
        Assert.Equal(0, calls.SeriesCount(indexer.Id));
        Assert.Equal(1, await EpisodePicksAsync(title));
    }

    [Fact]
    public async Task A_recheck_due_while_a_pass_runs_does_not_read_the_feeds_a_second_time()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted recheck overlap");
        var calls = new Calls();
        factory.Feeds.Films = i => calls.Film(i, []);
        factory.Feeds.Series = i => calls.Series(i, [Release(indexer, "recheck-overlap/1", "Breaking.Bad.S05E11.1080p.WEB-DL.DD5.1.H.264-BS", 81189, 0)]);

        await RunAsync(client);
        Assert.NotNull(State.PendingRecheck);

        // A pass holds the RSS lock: the recheck stands aside instead of reading the same feed at the same time.
        Assert.True(State.TryBeginRun());
        try
        {
            factory.Clock.Advance(Delay);
            calls.Clear();
            Assert.Equal("Nothing to recheck", (await RecheckAsync(client)).Summary);
            Assert.Equal(0, calls.SeriesCount(indexer.Id) + calls.FilmCount(indexer.Id));
            var manual = await client.PostAsync("/api/v1/rss/sync", null);
            Assert.False(manual.IsSuccessStatusCode);
        }
        finally
        {
            State.EndRun(factory.Clock.GetUtcNow().UtcDateTime);
        }
    }

    private RssFeedState State => factory.Services.GetRequiredService<RssFeedState>();

    private SchedulerState Scheduler => factory.Services.GetRequiredService<SchedulerState>();

    /// <summary>One app serves the whole class: every test starts without indexers or a planned recheck.</summary>
    private async Task<HttpClient> FreshAsync()
    {
        var client = await ClientAsync(factory);
        foreach (var indexer in (await client.GetFromJsonAsync<List<IndexerResponse>>("/api/v1/indexers", Json))!)
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }

        State.PlanRecheck(null);
        Scheduler.For(RssRecheckJob.JobName).NextRunAt = null;
        return client;
    }

    private static ReleaseInfo Release(IndexerResponse indexer, string guid, string title, int? tvdbId, int seeders) => new()
    {
        Guid = guid,
        Title = title,
        IndexerId = indexer.Id,
        IndexerName = indexer.Name,
        IndexerPriority = indexer.Priority,
        Protocol = DownloadProtocol.Torrent,
        Size = 2L * 1024 * 1024 * 1024,
        DownloadUrl = $"https://mock.invalid/download/{Math.Abs(guid.GetHashCode(StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture)}.torrent",
        Seeders = seeders,
        PublishDate = SeriesEndpointsTests.Now.AddHours(-1),
        TvdbId = tvdbId,
    };

    private static async Task<RssSyncResponse> RunAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/rss/sync", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RssSyncResponse>(Json))!;
    }

    private static async Task<TaskRunResponse> RecheckAsync(HttpClient client)
    {
        var response = await client.PostAsync($"/api/v1/system/tasks/{RssRecheckJob.JobName}/run", null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var run = (await response.Content.ReadFromJsonAsync<TaskRunResponse>(Json))!;
        Assert.True(run.Success, run.Error);
        return run;
    }

    private async Task<Bigarr.Domain.Shadow.ReleaseSighting> SightingAsync(string guid)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.ReleaseSightings.AsNoTracking().SingleAsync(s => s.Guid == guid);
    }

    private async Task<bool> FilmPickedAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.ShadowPicks.AnyAsync(p => p.ReleaseTitle == title);
    }

    private async Task<bool> EpisodePickedAsync(string title) => await EpisodePicksAsync(title) > 0;

    private async Task<int> EpisodePicksAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.SeriesShadowPicks.CountAsync(p => p.ReleaseTitle == title);
    }

    /// <summary>Feed requests per indexer, film and series apart.</summary>
    private sealed class Calls
    {
        private readonly Dictionary<(int, bool), int> _counts = [];

        public IReadOnlyList<ReleaseInfo> Film(Indexer indexer, IReadOnlyList<ReleaseInfo> releases) => Count(indexer, false, releases);

        public IReadOnlyList<ReleaseInfo> Series(Indexer indexer, IReadOnlyList<ReleaseInfo> releases) => Count(indexer, true, releases);

        public int FilmCount(int indexerId) => Get(indexerId, false);

        public int SeriesCount(int indexerId) => Get(indexerId, true);

        public void Clear()
        {
            lock (_counts)
            {
                _counts.Clear();
            }
        }

        private int Get(int indexerId, bool series)
        {
            lock (_counts)
            {
                return _counts.GetValueOrDefault((indexerId, series));
            }
        }

        private IReadOnlyList<ReleaseInfo> Count(Indexer indexer, bool series, IReadOnlyList<ReleaseInfo> releases)
        {
            lock (_counts)
            {
                _counts[(indexer.Id, series)] = _counts.GetValueOrDefault((indexer.Id, series)) + 1;
            }

            return releases;
        }
    }
}
