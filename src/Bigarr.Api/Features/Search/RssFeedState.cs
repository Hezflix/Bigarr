namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the RSS loop has seen, in memory (blueprint §3.5 keeps the queue as a projection too): the last few hundred
/// releases with their verdicts, the guids already processed so a release is judged once, and per-indexer health.
/// Lost on restart on purpose; grabs and history will be the durable trace from M4 on.
/// </summary>
public sealed class RssFeedState
{
    public const int MaxReleases = 500;

    public static readonly TimeSpan SeenRetention = TimeSpan.FromDays(7);

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly LinkedList<RssReleaseResponse> _releases = [];
    private readonly Dictionary<string, DateTime> _seen = [];
    private readonly Dictionary<int, RssIndexerStatusResponse> _indexers = [];

    public bool Running => _run.CurrentCount == 0;

    public DateTime? LastRunAt { get; private set; }

    public DateTime? NextRunAt { get; set; }

    /// <summary>Only one pass at a time, scheduled or manual.</summary>
    public bool TryBeginRun() => _run.Wait(0);

    public void EndRun(DateTime now)
    {
        lock (_lock)
        {
            LastRunAt = now;
        }

        _run.Release();
    }

    /// <summary>True the first time a (indexer, guid) pair shows up.</summary>
    public bool MarkSeen(int indexerId, string guid, DateTime now)
    {
        lock (_lock)
        {
            return _seen.TryAdd($"{indexerId}:{guid}", now);
        }
    }

    public void Add(RssReleaseResponse release)
    {
        lock (_lock)
        {
            _releases.AddFirst(release);
            while (_releases.Count > MaxReleases)
            {
                _releases.RemoveLast();
            }
        }
    }

    public void Prune(DateTime now)
    {
        lock (_lock)
        {
            foreach (var key in _seen.Where(kv => now - kv.Value > SeenRetention).Select(kv => kv.Key).ToList())
            {
                _seen.Remove(key);
            }
        }
    }

    public void RecordIndexer(RssIndexerStatusResponse status)
    {
        lock (_lock)
        {
            _indexers[status.IndexerId] = status;
        }
    }

    public void ForgetIndexer(int indexerId)
    {
        lock (_lock)
        {
            _indexers.Remove(indexerId);
        }
    }

    public IReadOnlyList<RssReleaseResponse> Releases(bool? matched, int limit)
    {
        lock (_lock)
        {
            IEnumerable<RssReleaseResponse> query = _releases;
            if (matched is not null)
            {
                query = query.Where(r => (r.Movie is not null) == matched.Value);
            }

            return query.Take(Math.Clamp(limit, 1, MaxReleases)).ToList();
        }
    }

    public RssStatusResponse Status(RssSyncOptions options)
    {
        lock (_lock)
        {
            return new RssStatusResponse(
                options.Enabled,
                (int)options.Interval.TotalMinutes,
                Running,
                LastRunAt,
                options.Enabled ? NextRunAt : null,
                _releases.Count,
                _releases.Count(r => r.Movie is not null),
                _releases.Count(r => r.AttributedVersionId is not null),
                _indexers.Values.OrderBy(i => i.Indexer).ToList());
        }
    }
}
