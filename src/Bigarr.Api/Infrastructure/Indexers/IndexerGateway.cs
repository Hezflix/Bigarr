using System.Collections.Concurrent;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers;

/// <summary>In-memory health of one indexer, served with the indexer row.</summary>
public sealed record IndexerStatus(bool Healthy, int Failures, DateTime? DisabledUntil, string? LastError, DateTime? LastFailureAt, DateTime? LastSuccessAt)
{
    public static IndexerStatus Fresh { get; } = new(true, 0, null, null, null, null);
}

/// <summary>
/// The single door to every indexer: picks the protocol client, spaces requests by the indexer's rate limit and
/// backs off on failures. Backoff state lives in memory and is lost on restart, on purpose.
/// </summary>
public sealed class IndexerGateway(IEnumerable<IIndexerClient> clients, TimeProvider time, ILogger<IndexerGateway> logger)
{
    /// <summary>Radarr's escalation: the indexer stays off for these many minutes after the nth consecutive failure.</summary>
    public static readonly int[] BackoffMinutes = [5, 15, 30, 60, 180, 360, 720, 1440];

    private readonly Dictionary<IndexerImplementation, IIndexerClient> _clients = clients.ToDictionary(c => c.Implementation);
    private readonly ConcurrentDictionary<int, RuntimeState> _states = new();

    public Task<IReadOnlyList<ReleaseInfo>> SearchAsync(Indexer indexer, MovieSearchCriteria criteria, CancellationToken cancellationToken) =>
        ExecuteAsync(indexer, (client, ct) => client.SearchAsync(indexer, criteria, ct), cancellationToken);

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentAsync(Indexer indexer, CancellationToken cancellationToken) =>
        ExecuteAsync(indexer, (client, ct) => client.FetchRecentAsync(indexer, ct), cancellationToken);

    /// <summary>
    /// A series search can take several requests (ids, then titles; "Title 05" per scene name for anime): each one waits
    /// for its own slot of the indexer's rate limit, and a failure backs the whole indexer off as usual.
    /// </summary>
    public Task<IReadOnlyList<ReleaseInfo>> SearchSeriesAsync(Indexer indexer, SeriesSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0, indexer.RateLimitSeconds));
        var state = State(indexer.Id);
        return ExecuteAsync(indexer, (client, ct) => client.SearchSeriesAsync(indexer, criteria, token => WaitAsync(indexer, state, interval, token), ct), cancellationToken, paced: false);
    }

    /// <summary>The capabilities (cached by the client), paced and backed off like any request.</summary>
    public Task<IndexerCapabilities> GetCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken) =>
        ExecuteAsync(indexer, (client, ct) => client.GetCapabilitiesAsync(indexer, ct), cancellationToken);

    /// <summary>
    /// The capabilities a search just read (the client caches them), without waiting for a slot; null when they cannot
    /// be had. For bookkeeping after a search, never before one.
    /// </summary>
    public async Task<IndexerCapabilities?> CachedCapabilitiesAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        try
        {
            return await ClientFor(indexer).GetCapabilitiesAsync(indexer, cancellationToken);
        }
        catch (IndexerException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<ReleaseInfo>> FetchRecentSeriesAsync(Indexer indexer, CancellationToken cancellationToken) =>
        ExecuteAsync(indexer, (client, ct) => client.FetchRecentSeriesAsync(indexer, ct), cancellationToken);

    /// <summary>Capabilities plus one feed fetch. Ignores the backoff: a test is the user asking "is it back?".</summary>
    public async Task<(IndexerCapabilities Capabilities, int RecentReleases)> TestAsync(Indexer indexer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var client = ClientFor(indexer);
        if (client is Torznab.TorznabClient torznab)
        {
            torznab.ForgetCapabilities(indexer);
        }

        try
        {
            var capabilities = await client.GetCapabilitiesAsync(indexer, cancellationToken);
            var recent = await client.FetchRecentAsync(indexer, cancellationToken);
            if (indexer.Id > 0)
            {
                State(indexer.Id).Succeeded(time.GetUtcNow());
            }

            return (capabilities, recent.Count);
        }
        catch (IndexerException ex) when (indexer.Id > 0)
        {
            State(indexer.Id).Failed(ex, time.GetUtcNow());
            throw;
        }
    }

    public IndexerStatus Status(int indexerId) =>
        _states.TryGetValue(indexerId, out var state) ? state.Snapshot(time.GetUtcNow()) : IndexerStatus.Fresh;

    public void Forget(int indexerId) => _states.TryRemove(indexerId, out _);

    private async Task<T> ExecuteAsync<T>(Indexer indexer, Func<IIndexerClient, CancellationToken, Task<T>> action, CancellationToken cancellationToken, bool paced = true)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        var client = ClientFor(indexer);
        var state = State(indexer.Id);
        var now = time.GetUtcNow();

        if (state.DisabledUntil is { } until && until > now)
        {
            var paused = new IndexerException(IndexerFailureKind.Disabled, $"{indexer.Name} is paused until {until:HH:mm} UTC after {state.Failures} failure(s): {state.LastError}");
            IndexerRequestTrace.RecordSkipped(indexer, paused);
            throw paused;
        }

        if (paced)
        {
            await WaitAsync(indexer, state, TimeSpan.FromSeconds(Math.Max(0, indexer.RateLimitSeconds)), cancellationToken);
        }

        try
        {
            var result = await action(client, cancellationToken);
            state.Succeeded(time.GetUtcNow());
            return result;
        }
        catch (IndexerException ex)
        {
            var disabledUntil = state.Failed(ex, time.GetUtcNow());
            logger.LogWarning("Indexer {Indexer} failed ({Kind}): {Message}. Paused until {Until:u}.", indexer.Name, ex.Kind, ex.Message, disabledUntil);
            throw;
        }
    }

    /// <summary>The indexer's rate limit, the wait told to the search's trace when there is one.</summary>
    private async Task WaitAsync(Indexer indexer, RuntimeState state, TimeSpan interval, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        await state.WaitForSlotAsync(interval, time, cancellationToken);
        IndexerRequestTrace.Waited(indexer.Id, time.GetElapsedTime(started));
    }

    private IIndexerClient ClientFor(Indexer indexer) =>
        _clients.TryGetValue(indexer.Implementation, out var client)
            ? client
            : throw new IndexerException(IndexerFailureKind.Request, $"No client for indexer implementation {indexer.Implementation}.");

    private RuntimeState State(int indexerId) => _states.GetOrAdd(indexerId, _ => new RuntimeState());

    private sealed class RuntimeState
    {
        private readonly SemaphoreSlim _slot = new(1, 1);
        private readonly Lock _lock = new();
        private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

        public int Failures { get; private set; }

        public DateTimeOffset? DisabledUntil { get; private set; }

        public string? LastError { get; private set; }

        public DateTimeOffset? LastFailureAt { get; private set; }

        public DateTimeOffset? LastSuccessAt { get; private set; }

        /// <summary>Requests to one indexer are spaced by its rate limit; they may still overlap once started.</summary>
        public async Task WaitForSlotAsync(TimeSpan interval, TimeProvider time, CancellationToken cancellationToken)
        {
            await _slot.WaitAsync(cancellationToken);
            try
            {
                var wait = _lastRequestAt + interval - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, time, cancellationToken);
                }

                _lastRequestAt = time.GetUtcNow();
            }
            finally
            {
                _slot.Release();
            }
        }

        public void Succeeded(DateTimeOffset now)
        {
            lock (_lock)
            {
                Failures = 0;
                DisabledUntil = null;
                LastError = null;
                LastSuccessAt = now;
            }
        }

        public DateTimeOffset Failed(IndexerException exception, DateTimeOffset now)
        {
            lock (_lock)
            {
                Failures++;
                LastError = exception.Message;
                LastFailureAt = now;
                var minutes = BackoffMinutes[Math.Min(Failures, BackoffMinutes.Length) - 1];
                DisabledUntil = now.AddMinutes(minutes);
                return DisabledUntil.Value;
            }
        }

        public IndexerStatus Snapshot(DateTimeOffset now)
        {
            lock (_lock)
            {
                var disabled = DisabledUntil is { } until && until > now;
                return new IndexerStatus(!disabled, Failures, disabled ? DisabledUntil!.Value.UtcDateTime : null, LastError, LastFailureAt?.UtcDateTime, LastSuccessAt?.UtcDateTime);
            }
        }
    }
}
