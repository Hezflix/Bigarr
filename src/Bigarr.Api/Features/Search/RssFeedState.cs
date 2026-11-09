namespace Bigarr.Api.Features.Search;

/// <summary>
/// What the RSS loop has seen, in memory (blueprint §3.5 keeps the queue as a projection too): the last few hundred
/// releases with their latest verdicts (one entry per release, however many passes judged it), the guids already
/// seen so a pass can tell what is new, and per-indexer health. Lost on restart on purpose; grabs and history are the
/// durable trace.
/// </summary>
public sealed class RssFeedState
{
    public const int MaxReleases = 500;

    public static readonly TimeSpan SeenRetention = TimeSpan.FromDays(7);

    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _run = new(1, 1);
    private readonly LinkedList<RssReleaseResponse> _releases = [];
    private readonly Dictionary<string, LinkedListNode<RssReleaseResponse>> _byKey = [];
    private readonly Dictionary<string, DateTime> _seen = [];
    private readonly Dictionary<int, RssIndexerStatusResponse> _indexers = [];

    public bool Running => _run.CurrentCount == 0;

    public DateTime? LastRunAt { get; private set; }

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

    /// <summary>
    /// A release judged by this pass: a new one goes on top of the list; one already listed keeps its place and when it
    /// was first seen, takes its new verdicts, and keeps the grab or pick an earlier pass made of it.
    /// </summary>
    public void Upsert(RssReleaseResponse release)
    {
        ArgumentNullException.ThrowIfNull(release);

        lock (_lock)
        {
            var key = Key(release);
            if (_byKey.TryGetValue(key, out var node))
            {
                node.Value = Merge(node.Value, release);
                return;
            }

            _byKey[key] = _releases.AddFirst(release);
            while (_releases.Count > MaxReleases)
            {
                _byKey.Remove(Key(_releases.Last!.Value));
                _releases.RemoveLast();
            }
        }
    }

    private static RssReleaseResponse Merge(RssReleaseResponse listed, RssReleaseResponse judged)
    {
        var sent = judged.GrabId is not null || judged.ShadowPickId is not null;
        var sentBefore = listed.GrabId is not null || listed.ShadowPickId is not null;
        return judged with
        {
            SeenAt = listed.SeenAt,
            GrabId = sent ? judged.GrabId : listed.GrabId,
            ShadowPickId = sent ? judged.ShadowPickId : listed.ShadowPickId,
            GrabError = !sent && sentBefore ? null : judged.GrabError,
        };
    }

    private static string Key(RssReleaseResponse release) => $"{release.IndexerId}:{release.Guid}";

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
                query = query.Where(r => (r.Movie is not null || r.Series is not null) == matched.Value);
            }

            return query.Take(Math.Clamp(limit, 1, MaxReleases)).ToList();
        }
    }

    /// <summary>The feed's side of the status; the scheduler's side (is it on, when is the next pass) comes from the caller.</summary>
    public RssStatusResponse Status(RssSyncOptions options, bool schedulerEnabled, DateTime? nextRunAt)
    {
        lock (_lock)
        {
            return new RssStatusResponse(
                options.Enabled,
                schedulerEnabled,
                (int)options.Interval.TotalMinutes,
                Running,
                LastRunAt,
                nextRunAt,
                _releases.Count,
                _releases.Count(r => r.Movie is not null || r.Series is not null),
                _releases.Count(r => r.AttributedVersionId is not null),
                _indexers.Values.OrderBy(i => i.Indexer).ToList(),
                _releases.Count(r => r.GrabId is not null),
                _releases.Count(r => r.ShadowPickId is not null));
        }
    }
}
