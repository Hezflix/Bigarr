using System.Diagnostics;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Indexers;

public sealed class IndexerGatewayTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly MovieSearchCriteria Criteria = MovieSearchCriteria.ForTitle("The Matrix", 1999);

    private static Indexer Indexer(int rateLimitSeconds = 0) => new()
    {
        Id = 42,
        Name = "Flaky",
        Implementation = IndexerImplementation.Torznab,
        BaseUrl = "http://flaky",
        RateLimitSeconds = rateLimitSeconds,
    };

    /// <summary>Scripted client: each call pops the next outcome.</summary>
    private sealed class ScriptedClient(Queue<Func<IReadOnlyList<ReleaseInfo>>> script) : IIndexerClient
    {
        public int Calls { get; private set; }

        public IndexerImplementation Implementation => IndexerImplementation.Torznab;

        public Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken) =>
            Task.FromResult(IndexerCapabilities.Everything);

        public Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(script.Dequeue()());
        }

        public Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken) => SearchAsync(indexer, Criteria, cancellationToken);

        /// <summary>A series search of three requests (ids, then two titles), each paced by the gateway.</summary>
        public async Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, Func<CancellationToken, Task> pace, CancellationToken cancellationToken)
        {
            for (var i = 0; i < 3; i++)
            {
                await pace(cancellationToken);
            }

            return await SearchAsync(indexer, Criteria, cancellationToken);
        }

        public Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken) => SearchAsync(indexer, Criteria, cancellationToken);
    }

    private static Func<IReadOnlyList<ReleaseInfo>> Ok() => () => [new ReleaseInfo { Guid = "g", Title = "t", IndexerName = "Flaky" }];

    private static Func<IReadOnlyList<ReleaseInfo>> Fail(IndexerFailureKind kind = IndexerFailureKind.ServerError) => () => throw new IndexerException(kind, $"boom {kind}");

    private static (IndexerGateway Gateway, ScriptedClient Client, MutableTimeProvider Time) Create(params Func<IReadOnlyList<ReleaseInfo>>[] script)
    {
        var client = new ScriptedClient(new Queue<Func<IReadOnlyList<ReleaseInfo>>>(script));
        var time = new MutableTimeProvider(Start);
        return (new IndexerGateway([client], time, NullLogger<IndexerGateway>.Instance), client, time);
    }

    [Fact]
    public async Task Failures_pause_the_indexer_with_an_escalating_backoff_and_a_success_resets_it()
    {
        var (gateway, client, time) = Create(Fail(), Fail(IndexerFailureKind.RateLimited), Ok(), Ok());
        var indexer = Indexer();

        Assert.True(gateway.Status(42).Healthy);

        var first = await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(indexer, Criteria, CancellationToken.None));
        Assert.Equal(IndexerFailureKind.ServerError, first.Kind);
        var status = gateway.Status(42);
        Assert.False(status.Healthy);
        Assert.Equal(1, status.Failures);
        Assert.Equal(Start.AddMinutes(5).UtcDateTime, status.DisabledUntil);
        Assert.Equal("boom ServerError", status.LastError);

        // While paused nothing reaches the client.
        var paused = await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(indexer, Criteria, CancellationToken.None));
        Assert.Equal(IndexerFailureKind.Disabled, paused.Kind);
        Assert.Contains("paused until 12:05", paused.Message);
        Assert.Equal(1, client.Calls);

        // Second failure: 15 minutes.
        time.Advance(TimeSpan.FromMinutes(6));
        await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(indexer, Criteria, CancellationToken.None));
        Assert.Equal(2, gateway.Status(42).Failures);
        Assert.Equal(time.Now.AddMinutes(15).UtcDateTime, gateway.Status(42).DisabledUntil);

        // Back after the pause, and a success clears everything.
        time.Advance(TimeSpan.FromMinutes(16));
        var releases = await gateway.SearchAsync(indexer, Criteria, CancellationToken.None);
        Assert.Single(releases);
        status = gateway.Status(42);
        Assert.True(status.Healthy);
        Assert.Equal(0, status.Failures);
        Assert.Null(status.DisabledUntil);
        Assert.Null(status.LastError);
        Assert.Equal(time.Now.UtcDateTime, status.LastSuccessAt);

        gateway.Forget(42);
        Assert.Equal(IndexerStatus.Fresh, gateway.Status(42));
    }

    [Fact]
    public async Task Backoff_is_capped_at_the_last_step()
    {
        var script = Enumerable.Range(0, IndexerGateway.BackoffMinutes.Length + 2).Select(_ => Fail()).ToArray();
        var (gateway, _, time) = Create(script);
        var indexer = Indexer();

        for (var i = 0; i < script.Length; i++)
        {
            if (i > 0)
            {
                time.Advance(TimeSpan.FromDays(2)); // past any pause
            }

            await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(indexer, Criteria, CancellationToken.None));
        }

        var status = gateway.Status(42);
        Assert.Equal(script.Length, status.Failures);
        Assert.False(status.Healthy);
        Assert.Equal(time.Now.AddMinutes(1440).UtcDateTime, status.DisabledUntil);
    }

    [Fact]
    public async Task Test_ignores_the_pause_and_clears_it_on_success()
    {
        var (gateway, client, _) = Create(Fail(), Ok(), Ok());
        var indexer = Indexer();

        await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(indexer, Criteria, CancellationToken.None));
        Assert.False(gateway.Status(42).Healthy);

        var (capabilities, recent) = await gateway.TestAsync(indexer, CancellationToken.None);
        Assert.True(capabilities.SupportsTmdbSearch);
        Assert.Equal(1, recent);
        Assert.Equal(2, client.Calls);
        Assert.True(gateway.Status(42).Healthy);
    }

    [Fact]
    public async Task Unknown_implementations_are_refused()
    {
        var (gateway, _, _) = Create(Ok());
        var mock = new Indexer { Id = 1, Name = "Mock", Implementation = IndexerImplementation.Mock };

        var ex = await Assert.ThrowsAsync<IndexerException>(() => gateway.SearchAsync(mock, Criteria, CancellationToken.None));
        Assert.Equal(IndexerFailureKind.Request, ex.Kind);
    }

    [Fact]
    public async Task Requests_to_one_indexer_are_spaced_by_its_rate_limit()
    {
        var client = new ScriptedClient(new Queue<Func<IReadOnlyList<ReleaseInfo>>>([Ok(), Ok(), Ok()]));
        var gateway = new IndexerGateway([client], TimeProvider.System, NullLogger<IndexerGateway>.Instance);
        var indexer = Indexer(rateLimitSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        await gateway.SearchAsync(indexer, Criteria, CancellationToken.None);
        await gateway.SearchAsync(indexer, Criteria, CancellationToken.None);
        await gateway.SearchAsync(indexer, Criteria, CancellationToken.None);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(1900), $"three calls took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Every_request_of_a_series_search_waits_for_its_own_slot()
    {
        var client = new ScriptedClient(new Queue<Func<IReadOnlyList<ReleaseInfo>>>([Ok()]));
        var gateway = new IndexerGateway([client], TimeProvider.System, NullLogger<IndexerGateway>.Instance);
        var indexer = Indexer(rateLimitSeconds: 1);

        var stopwatch = Stopwatch.StartNew();
        await gateway.SearchSeriesAsync(indexer, new SeriesSearchCriteria("Show", 1, null, null, Bigarr.Domain.Tv.SeriesType.Standard, []) { SeasonNumber = 1 }, CancellationToken.None);

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(1900), $"three paced requests took {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(1, client.Calls);
    }
}
