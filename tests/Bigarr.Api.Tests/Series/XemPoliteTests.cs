using System.Net;
using System.Net.Http.Headers;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Api.Infrastructure.Xem;
using Bigarr.Api.Tests.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Tests.Series;

/// <summary>TheXEM is a small free service: Bigarr asks only about shows that have a mapping, one request at a time, slowly, and backs off when told to.</summary>
public sealed class XemPoliteTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Only_series_on_the_havemap_list_are_asked_for_their_mapping()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1001, 1003]);
        await SeedAsync(db, 1001, 1002, 1003, 1004, 1005);
        var harness = Harness(db, xem);

        var summary = await harness.Service.RefreshAllAsync(CancellationToken.None);

        Assert.Equal(["/map/allNames", "/map/havemap", "/map/all?id=1001", "/map/all?id=1003"], xem.Requests.Select(r => r.Path).ToArray());
        Assert.Contains("2 mapping(s) fetched", summary);
        Assert.Contains("3 without a mapping (not asked)", summary);
    }

    [Fact]
    public async Task Requests_are_spaced_by_the_shared_limiter()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1, 2, 3, 4]);
        await SeedAsync(db, 1, 2, 3, 4);
        var harness = Harness(db, xem, interval: TimeSpan.FromSeconds(1));

        await harness.Service.RefreshAllAsync(CancellationToken.None);

        Assert.Equal(6, xem.Requests.Count);
        for (var i = 1; i < xem.Requests.Count; i++)
        {
            Assert.True(xem.Requests[i].At - xem.Requests[i - 1].At >= TimeSpan.FromSeconds(1), $"request {i} came too soon after the previous one");
        }
    }

    [Fact]
    public async Task A_429_with_retry_after_pauses_the_pass_then_resumes_with_the_same_show()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1, 2, 3]);
        xem.Refuse(2, times: 1, retryAfterSeconds: 30);
        await SeedAsync(db, 1, 2, 3);
        var harness = Harness(db, xem);

        var summary = await harness.Service.RefreshAllAsync(CancellationToken.None);

        var asked = xem.Requests.Where(r => r.Path.StartsWith("/map/all?", StringComparison.Ordinal)).ToList();
        Assert.Equal(["/map/all?id=1", "/map/all?id=2", "/map/all?id=2", "/map/all?id=3"], asked.Select(r => r.Path).ToArray());
        Assert.True(asked[2].At - asked[1].At >= TimeSpan.FromSeconds(30), "the pass kept to the Retry-After");
        Assert.Contains("3 mapping(s) fetched", summary);
        Assert.Contains("1 rate-limited answer(s)", summary);
        Assert.DoesNotContain("stopped", summary);
        Assert.Null(harness.Ids.LastFailure);
    }

    [Fact]
    public async Task Repeated_429s_stop_the_pass_and_the_next_pass_resumes_where_it_stopped()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1, 2, 3, 4]);
        xem.Refuse(3, times: int.MaxValue, retryAfterSeconds: 5);
        await SeedAsync(db, 1, 2, 3, 4);
        var harness = Harness(db, xem);

        var first = await harness.Service.RefreshAllAsync(CancellationToken.None);

        var askedAboutThree = xem.Requests.Count(r => r.Path == "/map/all?id=3");
        Assert.Equal(3, askedAboutThree);
        Assert.DoesNotContain(xem.Requests, r => r.Path == "/map/all?id=4");
        Assert.Contains("2 mapping(s) fetched", first);
        Assert.Contains("2 series left, the next pass resumes with Show 3 (tvdb 3)", first);
        Assert.NotNull(harness.Ids.LastFailure);

        // TheXEM is fine again, a day later is not even needed: the two read shows are fresh, the others are the oldest.
        xem.Refuse(3, times: 0, retryAfterSeconds: 0);
        xem.Requests.Clear();
        var second = await harness.Service.RefreshAllAsync(CancellationToken.None);

        Assert.Equal(["/map/all?id=3", "/map/all?id=4"], xem.Requests.Select(r => r.Path).Where(p => p.StartsWith("/map/all?", StringComparison.Ordinal)).ToArray());
        Assert.Contains("2 mapping(s) fetched", second);
        Assert.Null(harness.Ids.LastFailure);
    }

    [Fact]
    public async Task A_retry_after_longer_than_the_pass_may_wait_ends_it_at_once()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1, 2]);
        xem.Refuse(2, times: int.MaxValue, retryAfterSeconds: 3600);
        await SeedAsync(db, 1, 2);
        var harness = Harness(db, xem);

        var summary = await harness.Service.RefreshAllAsync(CancellationToken.None);

        Assert.Equal(1, xem.Requests.Count(r => r.Path == "/map/all?id=2"));
        Assert.Contains("asked to wait 60 min", summary);
        Assert.Contains("resumes with Show 2", summary);
    }

    [Fact]
    public async Task Maps_read_less_than_a_day_ago_are_not_fetched_again_and_the_rest_are()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1, 2]);
        await SeedAsync(db, 1, 2);
        var harness = Harness(db, xem);

        await harness.Service.RefreshAllAsync(CancellationToken.None);
        Assert.Equal(4, xem.Requests.Count);

        harness.Time.Advance(TimeSpan.FromHours(12));
        var kept = await harness.Service.RefreshAllAsync(CancellationToken.None);
        Assert.Equal(4, xem.Requests.Count);
        Assert.Contains("2 up to date", kept);
        Assert.Contains("0 mapping(s) fetched", kept);

        harness.Time.Advance(TimeSpan.FromHours(13));
        await harness.Service.RefreshAllAsync(CancellationToken.None);
        Assert.Equal(["/map/allNames", "/map/havemap", "/map/all?id=1", "/map/all?id=2"], xem.Requests.Skip(4).Select(r => r.Path).ToArray());
    }

    [Fact]
    public async Task A_pass_logs_one_summary_line_and_no_warning_per_show()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem(Enumerable.Range(1, 30).ToArray());
        xem.Refuse(10, times: int.MaxValue, retryAfterSeconds: 5);
        await SeedAsync(db, Enumerable.Range(1, 30).ToArray());
        var harness = Harness(db, xem);

        await harness.Service.RefreshAllAsync(CancellationToken.None);

        var line = Assert.Single(harness.Log.Entries, e => e.Level >= LogLevel.Information);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.StartsWith("TheXEM pass:", line.Message);
        Assert.DoesNotContain(harness.Log.Entries, e => e.Message.Contains("unavailable for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_series_refresh_while_theXEM_holds_requests_back_sends_nothing_and_fails_quietly()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var xem = new FakeXem([1]);
        xem.Refuse(1, times: 1, retryAfterSeconds: 120);
        await SeedAsync(db, 1);
        var harness = Harness(db, xem);
        var series = db.Series.Single();

        Assert.False(await harness.Service.RefreshSeriesAsync(series, [], CancellationToken.None));
        var sent = xem.Requests.Count;
        Assert.False(await harness.Service.RefreshSeriesAsync(series, [], CancellationToken.None));
        Assert.Equal(sent, xem.Requests.Count);
        Assert.DoesNotContain(harness.Log.Entries, e => e.Level >= LogLevel.Warning);

        // The pause is over: the next refresh goes through.
        harness.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(await harness.Service.RefreshSeriesAsync(series, [], CancellationToken.None));
    }

    [Fact]
    public async Task The_client_reads_retry_after_and_never_sends_while_paused()
    {
        var time = new VirtualTime(Start);
        var xem = new FakeXem([1]);
        xem.Refuse(1, times: 1, retryAfterSeconds: 42);
        var client = Client(xem, time, TimeSpan.Zero);

        var refused = await Assert.ThrowsAsync<XemRateLimitedException>(() => client.GetMappingsAsync(1, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(42), refused.RetryAfter);

        var held = await Assert.ThrowsAsync<XemRateLimitedException>(() => client.GetMappingsAsync(1, CancellationToken.None));
        Assert.Single(xem.Requests);
        Assert.True(held.RetryAfter <= TimeSpan.FromSeconds(42));

        time.Advance(TimeSpan.FromSeconds(43));
        Assert.NotEmpty(await client.GetMappingsAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task A_503_without_retry_after_holds_requests_for_the_default_pause()
    {
        var time = new VirtualTime(Start);
        var xem = new FakeXem([1]);
        xem.Refuse(1, times: 1, retryAfterSeconds: null, status: HttpStatusCode.ServiceUnavailable);
        var client = Client(xem, time, TimeSpan.Zero);

        var refused = await Assert.ThrowsAsync<XemRateLimitedException>(() => client.GetMappingsAsync(1, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(60), refused.RetryAfter);
    }

    [Fact]
    public async Task Requests_identify_Bigarr_with_its_version_and_repository()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddBigarrXem(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IXemClient));
        var agent = http.DefaultRequestHeaders.UserAgent.ToString();

        Assert.StartsWith($"Bigarr/{Bigarr.Api.Features.Server.SystemEndpoints.Version}", agent, StringComparison.Ordinal);
        Assert.Contains("(+https://github.com/Hezflix/Bigarr)", agent, StringComparison.Ordinal);
    }

    private static async Task SeedAsync(BigarrDbContext db, params int[] tvdbIds)
    {
        var root = new Bigarr.Domain.Library.RootFolder { Path = "/tv", Kind = Bigarr.Domain.Library.MediaKind.Series };
        foreach (var id in tvdbIds)
        {
            db.Series.Add(new Bigarr.Domain.Tv.Series { Title = $"Show {id}", SortTitle = $"show {id}", TvdbId = id, DefaultRootFolder = root });
        }

        await db.SaveChangesAsync();
    }

    private static XemClient Client(FakeXem xem, VirtualTime time, TimeSpan interval)
    {
        xem.Time = time;
        return new(new HttpClient(xem) { BaseAddress = new Uri("https://xem.test/map/") }, new XemThrottle(time, interval, TimeSpan.FromSeconds(60)));
    }

    private static (XemService Service, XemMappedIds Ids, VirtualTime Time, CapturingLogger Log) Harness(BigarrDbContext db, FakeXem xem, TimeSpan? interval = null)
    {
        var time = new VirtualTime(Start);
        var ids = new XemMappedIds(time);
        var log = new CapturingLogger();
        var options = Options.Create(new XemOptions());
        var service = new XemService(db, Client(xem, time, interval ?? TimeSpan.Zero), ids, options, time, log);
        return (service, ids, time, log);
    }

    /// <summary>A clock only the test moves; waiting on it (<c>Task.Delay</c>) moves it at once, so a pause costs no real time.</summary>
    private sealed class VirtualTime(DateTimeOffset start) : TimeProvider
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate)
            {
                _now += by;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new VirtualTimer(this, callback, state, dueTime);

        private sealed class VirtualTimer : ITimer
        {
            private readonly VirtualTime _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public VirtualTimer(VirtualTime owner, TimerCallback callback, object? state, TimeSpan dueTime)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    _owner.Advance(dueTime);
                    _ = Task.Run(() => _callback(_state));
                }

                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>A scripted TheXEM: <c>havemap</c> lists <c>mapped</c>, every mapped show has a one-row mapping, some can be made to refuse.</summary>
    private sealed class FakeXem(int[] mapped) : HttpMessageHandler
    {
        private readonly Dictionary<int, (int Times, int? RetryAfter, HttpStatusCode Status)> _refusals = [];

        public VirtualTime Time { get; set; } = null!;

        public List<(string Path, DateTimeOffset At)> Requests { get; } = [];

        public void Refuse(int tvdbId, int times, int? retryAfterSeconds, HttpStatusCode status = HttpStatusCode.TooManyRequests) =>
            _refusals[tvdbId] = (times, retryAfterSeconds, status);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add((uri.AbsolutePath == "/map/all" ? $"/map/all?id={System.Web.HttpUtility.ParseQueryString(uri.Query)["id"]}" : uri.AbsolutePath, Time.GetUtcNow()));
            var body = uri.AbsolutePath switch
            {
                "/map/havemap" => $$"""{"result":"success","data":[{{string.Join(",", mapped.Select(i => $"\"{i}\""))}}],"message":""}""",
                "/map/allNames" => $$"""{"result":"success","data":{"{{mapped.FirstOrDefault()}}":[{"Some Show":1}]},"message":""}""",
                _ => """{"result":"success","data":[{"scene":{"season":1,"episode":1,"absolute":1},"tvdb":{"season":1,"episode":1,"absolute":1}}],"message":""}""",
            };

            if (uri.AbsolutePath == "/map/all"
                && int.TryParse(System.Web.HttpUtility.ParseQueryString(uri.Query)["id"], out var id)
                && _refusals.TryGetValue(id, out var refusal) && refusal.Times > 0)
            {
                _refusals[id] = refusal with { Times = refusal.Times == int.MaxValue ? int.MaxValue : refusal.Times - 1 };
                var refused = new HttpResponseMessage(refusal.Status);
                if (refusal.RetryAfter is { } seconds)
                {
                    refused.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
                }

                return Task.FromResult(refused);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CapturingLogger : ILogger<XemService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
