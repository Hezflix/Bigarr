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

public sealed class RssSightingsApiFactory() : BigarrApiFactory(null, new Dictionary<string, string> { ["Bigarr:Shadow:Enabled"] = "true" }, deleteOnDispose: true, time: new FixedTimeProvider(SeriesEndpointsTests.Now))
{
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

    private static ReleaseInfo Release(IndexerResponse indexer, string guid, string title, int? tvdbId) => Release(indexer.Id, indexer.Name, indexer.Priority, guid, title, tvdbId);

    private static ReleaseInfo Release(Indexer indexer, string guid, string title, int? tvdbId) => Release(indexer.Id, indexer.Name, indexer.Priority, guid, title, tvdbId);

    private static ReleaseInfo Release(int indexerId, string indexerName, int priority, string guid, string title, int? tvdbId) => new()
    {
        Guid = guid,
        Title = title,
        IndexerId = indexerId,
        IndexerName = indexerName,
        IndexerPriority = priority,
        Protocol = DownloadProtocol.Torrent,
        Size = 2L * 1024 * 1024 * 1024,
        DownloadUrl = $"https://mock.invalid/download/{Math.Abs(guid.GetHashCode(StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture)}.torrent",
        Seeders = 10,
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
