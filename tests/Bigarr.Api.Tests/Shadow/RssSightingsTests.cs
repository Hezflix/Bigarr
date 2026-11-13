using System.Globalization;
using System.Net.Http.Json;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Search;
using Bigarr.Api.Tests.Series;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Bigarr.Api.Tests.Import.ImportTestHelpers;

namespace Bigarr.Api.Tests.Shadow;

/// <summary>
/// The mock indexer's place is taken by a client whose film and series feeds a test writes by hand, to put the same
/// release in several feeds or in several passes, which is what real indexers do.
/// </summary>
public sealed class ScriptedFeedClient : IIndexerClient
{
    public Func<Indexer, IReadOnlyList<ReleaseInfo>> Films { get; set; } = _ => [];

    public Func<Indexer, IReadOnlyList<ReleaseInfo>> Series { get; set; } = _ => [];

    public IndexerImplementation Implementation => IndexerImplementation.Mock;

    public Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken) => Task.FromResult(IndexerCapabilities.Everything);

    public Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReleaseInfo>>([]);

    public Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, Func<CancellationToken, Task> pace, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReleaseInfo>>([]);

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken) => Task.FromResult(Films(indexer));

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken) => Task.FromResult(Series(indexer));
}

public sealed class RssSightingsApiFactory : BigarrApiFactory
{
    public RssSightingsApiFactory()
        : this(new MutableTimeProvider(SeriesEndpointsTests.Now))
    {
    }

    private RssSightingsApiFactory(MutableTimeProvider clock)
        : base(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: clock) => Clock = clock;

    /// <summary>The test's clock: a pass sees a pick only once time has moved past it, as in a real run.</summary>
    public MutableTimeProvider Clock { get; }

    public ScriptedFeedClient Feeds { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IIndexerClient>();
            services.AddSingleton<IIndexerClient>(Feeds);
        });
    }
}

/// <summary>
/// A release is written down once per (indexer, guid), whatever feed brings it and however many passes see it again:
/// a pass never fails on a sighting it already holds, and what a later feed teaches (the release is an episode of a
/// show of the library) completes the row instead of colliding with it.
/// </summary>
public sealed class RssSightingsTests(RssSightingsApiFactory factory) : IClassFixture<RssSightingsApiFactory>
{
    private const string BreakingBad = "Breaking.Bad.S05E01.1080p.WEB-DL.DD5.1.H.264-BS";

    [Fact]
    public async Task A_release_in_the_film_and_the_series_feed_of_one_pass_is_one_sighting_that_keeps_the_series_match()
    {
        var client = await FreshAsync();
        var series = await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted both feeds");
        var release = Release(indexer, "both-feeds/1", BreakingBad, tvdbId: 81189);
        factory.Feeds.Films = _ => [release];
        factory.Feeds.Series = _ => [release];

        var pass = await RunAsync(client);

        Assert.Equal(2, pass.Fetched); // listed in two feeds, counted in each
        var sighting = await SingleAsync("both-feeds/1");
        Assert.Equal(81189, sighting.TvdbId);
        Assert.NotNull(sighting.Episodes);
        Assert.Null(sighting.UnmatchedReason);
        var entry = (await client.GetFromJsonAsync<List<RssReleaseResponse>>("/api/v1/rss/releases", Json))!.Single(r => r.Guid == "both-feeds/1");
        Assert.Equal(series.Id, entry.Series!.Id);
    }

    [Fact]
    public async Task A_release_already_sighted_is_updated_not_inserted_again_and_gains_the_match_a_later_pass_finds()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted two passes");
        var release = Release(indexer, "two-passes/1", BreakingBad, tvdbId: null);

        // First pass: only the film feed carries it, nothing of the library matches.
        factory.Feeds.Films = _ => [release];
        factory.Feeds.Series = _ => [];
        await RunAsync(client);
        var first = await SingleAsync("two-passes/1");
        Assert.Null(first.TvdbId);
        Assert.NotNull(first.UnmatchedReason);

        // Second pass: the series feed carries it too; the row is completed, not duplicated.
        factory.Feeds.Series = _ => [release];
        await RunAsync(client);
        var second = await SingleAsync("two-passes/1");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.SeenAt, second.SeenAt);
        Assert.Equal(81189, second.TvdbId);
        Assert.Null(second.UnmatchedReason);
        Assert.NotEmpty(second.Verdicts);

        // Third pass, nothing new: still that row, untouched.
        await RunAsync(client);
        var third = await SingleAsync("two-passes/1");
        Assert.Equal(second.Id, third.Id);
        Assert.Equal(81189, third.TvdbId);
    }

    [Fact]
    public async Task A_release_nothing_matches_listed_by_both_feeds_and_seen_again_stays_one_unmatched_sighting()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted unmatched");
        var release = Release(indexer, "unmatched/1", "Some.Unknown.Show.S01E01.1080p.WEB.h264-GRP", tvdbId: null);
        factory.Feeds.Films = _ => [release];
        factory.Feeds.Series = _ => [release];

        await RunAsync(client);
        await RunAsync(client);

        var sighting = await SingleAsync("unmatched/1");
        Assert.Null(sighting.TvdbId);
        Assert.Null(sighting.TmdbId);
        Assert.NotNull(sighting.UnmatchedReason);
    }

    [Fact]
    public async Task A_guid_longer_than_the_column_is_sighted_once_across_passes()
    {
        var client = await FreshAsync();
        var indexer = await AddMockIndexerAsync(client, "Scripted long guid");
        var guid = "https://indexer.invalid/details/" + new string('x', 1200);
        factory.Feeds.Films = _ => [Release(indexer, guid, "Some.Other.Film.2019.1080p.BluRay.x264-GRP", tvdbId: null)];
        factory.Feeds.Series = _ => [];

        await RunAsync(client);
        await RunAsync(client);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        Assert.Equal(1, await db.ReleaseSightings.CountAsync(s => s.Title == "Some.Other.Film.2019.1080p.BluRay.x264-GRP"));
    }

    [Fact]
    public async Task One_indexer_failing_does_not_stop_the_sightings_of_the_others()
    {
        var client = await FreshAsync();
        var good = await AddMockIndexerAsync(client, "Scripted good");
        var bad = await AddMockIndexerAsync(client, "Scripted bad");
        factory.Feeds.Films = indexer => indexer.Id == bad.Id
            ? throw new IndexerException(IndexerFailureKind.ServerError, "boom")
            : [Release(indexer, "good/1", "Another.Film.2018.1080p.BluRay.x264-GRP", tvdbId: null)];
        factory.Feeds.Series = _ => [];

        var pass = await RunAsync(client);

        Assert.Equal(good.Id, (await SingleAsync("good/1")).IndexerId);
        Assert.Contains(pass.IndexerErrors, e => e.IndexerId == bad.Id);
    }

    [Fact]
    public async Task A_film_release_turned_down_for_seeders_is_judged_again_and_picked_once_it_has_some()
    {
        var client = await FreshAsync();
        var (hd, _) = await ProfilesAsync(client);
        var root = await AddRootAsync(client, Directory.CreateTempSubdirectory("bigarr-rss-rejudge-").FullName);
        await AddMovieAsync(client, 603, hd, root.Id);
        var indexer = await AddMockIndexerAsync(client, "Scripted seeders film");
        var title = "The.Matrix.1999.1080p.BluRay.x264-GRP";

        factory.Feeds.Films = _ => [Release(indexer, "seeders-film/1", title, tvdbId: null, seeders: 0)];
        factory.Feeds.Series = _ => [];
        var first = await RunAsync(client);
        Assert.Equal(0, first.Picked);
        var rejected = await SingleAsync("seeders-film/1");
        var verdict = Assert.Single(rejected.Verdicts);
        Assert.False(verdict.Approved);
        Assert.Equal(["Not enough seeders: 0 (minimum 1)"], verdict.Rejections);

        factory.Feeds.Films = _ => [Release(indexer, "seeders-film/1", title, tvdbId: null, seeders: 5)];
        var second = await RunAsync(client);
        Assert.Equal(1, second.Picked);
        var approved = await SingleAsync("seeders-film/1");
        Assert.Equal(rejected.Id, approved.Id);
        Assert.True(Assert.Single(approved.Verdicts).Approved);
        Assert.NotNull(approved.AttributedVersion);
        Assert.True(await PickedAsync(title));
    }

    [Fact]
    public async Task An_episode_release_turned_down_for_seeders_is_judged_again_and_picked_once_it_has_some()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted seeders series");
        var title = "Breaking.Bad.S05E02.1080p.WEB-DL.DD5.1.H.264-BS";

        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "seeders-series/1", title, tvdbId: 81189, seeders: 0)];
        var first = await RunAsync(client);
        Assert.Equal(0, first.Picked);
        var rejected = await SingleAsync("seeders-series/1");
        Assert.All(rejected.Verdicts, v => Assert.Equal(["Not enough seeders: 0 (minimum 1)"], v.Rejections));

        factory.Feeds.Series = _ => [Release(indexer, "seeders-series/1", title, tvdbId: 81189, seeders: 5)];
        var second = await RunAsync(client);
        Assert.Equal(1, second.Picked);
        var approved = await SingleAsync("seeders-series/1");
        Assert.Equal(rejected.Id, approved.Id);
        Assert.Contains(approved.Verdicts, v => v.Approved);
        Assert.NotNull(approved.AttributedVersion);
        Assert.True(await SeriesPickedAsync(title));
    }

    [Fact]
    public async Task A_release_turned_down_for_good_keeps_its_first_verdict()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted permanent");
        // 2160p is not in the HD profile, whatever the seeders say.
        var title = "Breaking.Bad.S05E03.2160p.WEB-DL.DD5.1.H.264-BS";

        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "permanent/1", title, tvdbId: 81189, seeders: 5)];
        await RunAsync(client);
        var first = await SingleAsync("permanent/1");
        Assert.All(first.Verdicts, v => Assert.False(v.Approved));

        factory.Feeds.Series = _ => [Release(indexer, "permanent/1", title, tvdbId: 81189, seeders: 50)];
        await RunAsync(client);
        var second = await SingleAsync("permanent/1");
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public async Task A_release_turned_down_for_seeders_and_for_good_keeps_its_first_verdict()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted mixed");
        var title = "Breaking.Bad.S05E04.2160p.WEB-DL.DD5.1.H.264-BS";

        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "mixed/1", title, tvdbId: 81189, seeders: 0)];
        await RunAsync(client);
        var first = await SingleAsync("mixed/1");
        Assert.All(first.Verdicts, v => Assert.Contains(v.Rejections, r => r.StartsWith("Not enough seeders", StringComparison.Ordinal)));
        Assert.All(first.Verdicts, v => Assert.True(v.Rejections.Count > 1));

        factory.Feeds.Series = _ => [Release(indexer, "mixed/1", title, tvdbId: 81189, seeders: 5)];
        await RunAsync(client);
        var second = await SingleAsync("mixed/1");
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public async Task A_release_turned_down_for_seeders_keeps_that_reason_when_the_episode_was_taken_by_another_meanwhile()
    {
        var client = await FreshAsync();
        await SeriesSetup.AddAsync(client, 81189);
        var indexer = await AddMockIndexerAsync(client, "Scripted taken meanwhile");
        var late = "Breaking.Bad.S05E05.1080p.WEB-DL.DD5.1.H.264-BS";
        var other = "Breaking.Bad.S05E05.1080p.WEB-DL.DD5.1.H.264-OTHER";

        factory.Feeds.Films = _ => [];
        factory.Feeds.Series = _ => [Release(indexer, "taken/late", late, tvdbId: 81189, seeders: 0)];
        await RunAsync(client);
        var first = await SingleAsync("taken/late");

        // Another release of the episode is picked first: the same quality is no upgrade on it any more.
        factory.Feeds.Series = _ => [Release(indexer, "taken/other", other, tvdbId: 81189)];
        Assert.Equal(1, (await RunAsync(client)).Picked);
        factory.Clock.Advance(TimeSpan.FromMinutes(15));

        factory.Feeds.Series = _ => [Release(indexer, "taken/late", late, tvdbId: 81189, seeders: 5)];
        Assert.Equal(0, (await RunAsync(client)).Picked);
        var third = await SingleAsync("taken/late");
        Assert.Equal(Describe(first), Describe(third));
        Assert.All(third.Verdicts, v => Assert.Equal(["Not enough seeders: 0 (minimum 1)"], v.Rejections));
    }

    private static string Describe(Bigarr.Domain.Shadow.ReleaseSighting sighting) =>
        string.Join(" | ", sighting.Verdicts.Select(v => $"{v.VersionName} {v.Approved} {string.Join(", ", v.Rejections)}")) + $" -> {sighting.AttributedVersion}";

    private async Task<bool> PickedAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.ShadowPicks.AnyAsync(p => p.ReleaseTitle == title);
    }

    private async Task<bool> SeriesPickedAsync(string title)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.SeriesShadowPicks.AnyAsync(p => p.ReleaseTitle == title);
    }

    /// <summary>One app serves the whole class: every test starts without indexers, so only its own feeds are read.</summary>
    private async Task<HttpClient> FreshAsync()
    {
        var client = await ClientAsync(factory);
        foreach (var indexer in (await client.GetFromJsonAsync<List<IndexerResponse>>("/api/v1/indexers", Json))!)
        {
            await client.DeleteAsync($"/api/v1/indexers/{indexer.Id}");
        }

        return client;
    }

    private static ReleaseInfo Release(IndexerResponse indexer, string guid, string title, int? tvdbId, int seeders = 10) => Release(indexer.Id, indexer.Name, indexer.Priority, guid, title, tvdbId, seeders);

    private static ReleaseInfo Release(Indexer indexer, string guid, string title, int? tvdbId) => Release(indexer.Id, indexer.Name, indexer.Priority, guid, title, tvdbId);

    private static ReleaseInfo Release(int indexerId, string indexerName, int priority, string guid, string title, int? tvdbId, int seeders = 10) => new()
    {
        Guid = guid,
        Title = title,
        IndexerId = indexerId,
        IndexerName = indexerName,
        IndexerPriority = priority,
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

    private async Task<Bigarr.Domain.Shadow.ReleaseSighting> SingleAsync(string guid)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BigarrDbContext>();
        return await db.ReleaseSightings.AsNoTracking().SingleAsync(s => s.Guid == guid);
    }
}
