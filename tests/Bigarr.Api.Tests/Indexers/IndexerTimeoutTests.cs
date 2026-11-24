using Bigarr.Api.Features.Search;
using Bigarr.Api.Infrastructure.Indexers;
using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Domain.Indexers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bigarr.Api.Tests.Indexers;

public sealed class IndexerTimeoutTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static Indexer Indexer() => new()
    {
        Id = 7,
        Name = "Silent",
        Implementation = IndexerImplementation.Torznab,
        BaseUrl = "http://silent:9696/1",
        ApiKey = "k3y",
        RateLimitSeconds = 0,
    };

    /// <summary>Accepts the request and never answers, until the caller or the client's own timeout cancels it.</summary>
    private sealed class SilentHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        }
    }

    private static (IndexerGateway Gateway, SilentHandler Handler) Create(TimeSpan requestTimeout)
    {
        var handler = new SilentHandler();
        var time = new MutableTimeProvider(Start);
        var client = new TorznabClient(new StubHttpClientFactory(handler, requestTimeout), time, NullLogger<TorznabClient>.Instance);
        return (new IndexerGateway([client], time, NullLogger<IndexerGateway>.Instance), handler);
    }

    [Fact]
    public void The_request_timeout_is_shorter_than_the_search_deadline()
    {
        Assert.True(IndexerQueries.PerIndexerTimeout - TorznabClient.RequestTimeout >= TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task An_indexer_that_never_answers_is_paused_after_its_first_timeout()
    {
        var (gateway, handler) = Create(TimeSpan.FromMilliseconds(200));
        var indexer = Indexer();

        var first = await IndexerQueries.RunAsync([indexer], (i, token) => gateway.FetchRecentAsync(i, token), CancellationToken.None);

        var error = Assert.IsType<IndexerException>(first[0].Error);
        Assert.Equal(IndexerFailureKind.Unreachable, error.Kind);
        Assert.Contains("did not answer", error.Message, StringComparison.Ordinal);
        var status = gateway.Status(indexer.Id);
        Assert.False(status.Healthy);
        Assert.Equal(1, status.Failures);
        Assert.NotNull(status.DisabledUntil);

        var calls = handler.Calls;
        var second = await IndexerQueries.RunAsync([indexer], (i, token) => gateway.FetchRecentAsync(i, token), CancellationToken.None);

        Assert.Equal(IndexerFailureKind.Disabled, second[0].Error!.Kind);
        Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public async Task A_search_the_user_leaves_does_not_pause_the_indexer()
    {
        var (gateway, handler) = Create(TimeSpan.FromSeconds(30));
        var indexer = Indexer();
        using var user = new CancellationTokenSource();
        user.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => IndexerQueries.RunAsync([indexer], (i, token) => gateway.FetchRecentAsync(i, token), user.Token));

        Assert.Equal(1, handler.Calls);
        var status = gateway.Status(indexer.Id);
        Assert.True(status.Healthy);
        Assert.Equal(0, status.Failures);
    }
}
