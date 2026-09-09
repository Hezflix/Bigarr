using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bigarr.Api.Features.Indexers;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Indexers.Prowlarr;
using Bigarr.Api.Tests.Auth;
using Bigarr.Api.Tests.Metadata;
using Bigarr.Api.Tests.Persistence;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Indexers;

public sealed class ProwlarrSyncTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private sealed class FakeProwlarr : IProwlarrClient
    {
        public List<ProwlarrIndexer> Indexers { get; } = [];

        public IndexerException? Failure { get; set; }

        public Task<ProwlarrSystemStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new ProwlarrSystemStatus { AppName = "Prowlarr" });

        public Task<IReadOnlyList<ProwlarrIndexer>> GetIndexersAsync(CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult<IReadOnlyList<ProwlarrIndexer>>(Indexers.ToList()) : throw Failure;
    }

    private static ProwlarrIndexer Remote(int id, string name, bool enable = true, string protocol = "torrent", int priority = 25) =>
        new() { Id = id, Name = name, Enable = enable, Protocol = protocol, Priority = priority };

    private (ProwlarrSyncService Service, FakeProwlarr Prowlarr, ProwlarrSyncState State, Infrastructure.Persistence.BigarrDbContext Db) Create(bool configured = true)
    {
        var db = _database.CreateContext();
        var prowlarr = new FakeProwlarr();
        var state = new ProwlarrSyncState();
        var options = Options.Create(new ProwlarrOptions { BaseUrl = configured ? "https://prowlarr.example/" : null, ApiKey = configured ? "pr0wl" : null });
        var gateway = new IndexerGateway([], new FixedTimeProvider(Now), NullLogger<IndexerGateway>.Instance);
        var service = new ProwlarrSyncService(db, prowlarr, options, state, gateway, new FixedTimeProvider(Now), NullLogger<ProwlarrSyncService>.Instance);
        return (service, prowlarr, state, db);
    }

    [Fact]
    public async Task Torrent_indexers_are_imported_usenet_skipped_and_the_state_recorded()
    {
        var (service, prowlarr, state, db) = Create();
        prowlarr.Indexers.AddRange([Remote(1, "Tracker One", priority: 10), Remote(7, "Public One", enable: false, priority: 60), Remote(12, "NZB Geek", protocol: "usenet")]);

        var result = await service.SyncAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((3, 2, 0, 0, 1), (result.Value!.Total, result.Value.Added, result.Value.Updated, result.Value.Removed, result.Value.Skipped));
        Assert.Equal(["NZB Geek (usenet)"], result.Value.SkippedNames);

        var rows = db.Indexers.OrderBy(i => i.ProwlarrId).ToList();
        Assert.Equal(2, rows.Count);
        var one = rows[0];
        Assert.Equal("Tracker One", one.Name);
        Assert.Equal(IndexerImplementation.Torznab, one.Implementation);
        Assert.Equal("https://prowlarr.example/1", one.BaseUrl);
        Assert.Equal("/api", one.ApiPath);
        Assert.Equal("pr0wl", one.ApiKey);
        Assert.Equal(10, one.Priority);
        Assert.True(one.EnableRss && one.EnableAutomaticSearch && one.EnableInteractiveSearch);
        Assert.Equal(1, one.ProwlarrId);
        Assert.Equal(Indexer.DefaultCategories, one.Categories);

        var disabled = rows[1];
        Assert.Equal(50, disabled.Priority); // clamped
        Assert.False(disabled.EnableRss || disabled.EnableAutomaticSearch || disabled.EnableInteractiveSearch);

        Assert.Equal(Now.UtcDateTime, state.LastSyncAt);
        Assert.Null(state.LastError);
        Assert.Equal(3, state.LastResult!.Total);

        var status = service.Status();
        Assert.True(status.Configured);
        Assert.Equal("prowlarr.example", status.Host);
        Assert.Equal(2, status.LastResult!.Added);
    }

    [Fact]
    public async Task A_second_sync_refreshes_prowlarr_fields_keeps_local_tuning_and_removes_vanished_indexers()
    {
        var (service, prowlarr, _, db) = Create();
        prowlarr.Indexers.AddRange([Remote(1, "Tracker One", priority: 10), Remote(2, "Tracker Two")]);
        Assert.True((await service.SyncAsync(CancellationToken.None)).IsSuccess);

        // Local tuning on the first, and the user disables interactive search on the second.
        var one = db.Indexers.Single(i => i.ProwlarrId == 1);
        one.MinimumSeeders = 5;
        one.RateLimitSeconds = 10;
        one.Categories = [2040];
        var two = db.Indexers.Single(i => i.ProwlarrId == 2);
        two.EnableInteractiveSearch = false;
        await db.SaveChangesAsync();

        prowlarr.Indexers.Clear();
        prowlarr.Indexers.AddRange([Remote(1, "Tracker One Renamed", priority: 3), Remote(3, "Tracker Three")]);
        var result = await service.SyncAsync(CancellationToken.None);

        Assert.Equal((2, 1, 1, 1, 0), (result.Value!.Total, result.Value.Added, result.Value.Updated, result.Value.Removed, result.Value.Skipped));
        Assert.Empty(result.Value.SkippedNames);

        await using var fresh = _database.CreateContext();
        var rows = fresh.Indexers.OrderBy(i => i.ProwlarrId).ToList();
        Assert.Equal([1, 3], rows.Select(r => r.ProwlarrId));
        Assert.Equal("Tracker One Renamed", rows[0].Name);
        Assert.Equal(3, rows[0].Priority);
        Assert.Equal(5, rows[0].MinimumSeeders);
        Assert.Equal(10, rows[0].RateLimitSeconds);
        Assert.Equal([2040], rows[0].Categories);
        Assert.DoesNotContain(rows, r => r.Name == "Tracker Two");
    }

    [Fact]
    public async Task Disabling_in_prowlarr_turns_every_mode_off_and_re_enabling_turns_them_back_on()
    {
        var (service, prowlarr, _, db) = Create();
        prowlarr.Indexers.Add(Remote(1, "Tracker One"));
        await service.SyncAsync(CancellationToken.None);

        prowlarr.Indexers[0] = Remote(1, "Tracker One", enable: false);
        await service.SyncAsync(CancellationToken.None);
        var row = db.Indexers.Single();
        Assert.False(row.EnableRss || row.EnableAutomaticSearch || row.EnableInteractiveSearch);

        prowlarr.Indexers[0] = Remote(1, "Tracker One", enable: true);
        await service.SyncAsync(CancellationToken.None);
        Assert.True(row.EnableRss && row.EnableAutomaticSearch && row.EnableInteractiveSearch);
    }

    [Fact]
    public async Task Names_taken_by_hand_made_indexers_get_a_suffix()
    {
        var (service, prowlarr, _, db) = Create();
        db.Indexers.Add(new Indexer { Name = "Tracker One", Implementation = IndexerImplementation.Mock });
        await db.SaveChangesAsync();
        prowlarr.Indexers.Add(Remote(1, "Tracker One"));

        await service.SyncAsync(CancellationToken.None);

        Assert.Equal(["Tracker One", "Tracker One (Prowlarr)"], db.Indexers.OrderBy(i => i.Id).Select(i => i.Name));
    }

    [Fact]
    public async Task Failures_and_missing_configuration_are_reported_without_touching_the_rows()
    {
        var (service, prowlarr, state, db) = Create();
        prowlarr.Indexers.Add(Remote(1, "Tracker One"));
        await service.SyncAsync(CancellationToken.None);

        prowlarr.Failure = new IndexerException(IndexerFailureKind.Authentication, "the indexer redirected to sso.example");
        var failed = await service.SyncAsync(CancellationToken.None);
        Assert.False(failed.IsSuccess);
        Assert.Equal(502, failed.Failure!.StatusCode);
        Assert.Contains("sso.example", failed.Failure.Detail);
        Assert.Equal("the indexer redirected to sso.example", state.LastError);
        Assert.Single(db.Indexers);

        var (unconfigured, _, _, _) = Create(configured: false);
        var refused = await unconfigured.SyncAsync(CancellationToken.None);
        Assert.Equal(400, refused.Failure!.StatusCode);
        Assert.False(unconfigured.Status().Configured);
        Assert.Null(unconfigured.Status().Host);
    }
}

public sealed class ProwlarrEndpointsTests(BigarrApiFactory factory) : IClassFixture<BigarrApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public async Task Without_configuration_the_status_says_so_and_a_sync_is_refused()
    {
        var client = factory.CreateClientWithCookies();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = BigarrApiFactory.AdminUsername, password = BigarrApiFactory.AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var status = (await client.GetFromJsonAsync<ProwlarrStatusResponse>("/api/v1/indexers/prowlarr", Json))!;
        Assert.False(status.Configured);
        Assert.Null(status.Host);

        var sync = await client.PostAsync("/api/v1/indexers/prowlarr/sync", null);
        Assert.Equal(HttpStatusCode.BadRequest, sync.StatusCode);
        Assert.Contains("Prowlarr is not configured", await sync.Content.ReadAsStringAsync());

        var indexers = (await client.GetFromJsonAsync<List<IndexerResponse>>("/api/v1/indexers", Json))!;
        Assert.All(indexers, i => Assert.Null(i.ProwlarrId));
    }
}
