namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>TheXEM asked to slow down (HTTP 429 or 503): nothing is sent to it again before <see cref="RetryAfter"/> has passed.</summary>
public sealed class XemRateLimitedException(TimeSpan retryAfter, string message) : HttpRequestException(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The one limiter every request to TheXEM goes through (a singleton, shared by the slow pass and the per-show
/// refreshes): one request at a time, a minimum pause after each, and a hold on everything once TheXEM said
/// <c>Retry-After</c>. While held, callers get a <see cref="XemRateLimitedException"/> at once, without a request.
/// </summary>
public sealed class XemThrottle(TimeProvider time, TimeSpan? minInterval = null, TimeSpan? defaultRetryAfter = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval = minInterval ?? TimeSpan.FromSeconds(1);
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public DateTimeOffset UtcNow => time.GetUtcNow();

    /// <summary>The pause used when TheXEM refuses without saying for how long.</summary>
    public TimeSpan DefaultRetryAfter { get; } = defaultRetryAfter ?? TimeSpan.FromSeconds(60);

    /// <summary>Waits for this request's turn. Dispose the lease once the answer has been read.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_pausedUntil > now)
            {
                throw new XemRateLimitedException(_pausedUntil - now, "TheXEM asked to slow down: no request is sent until its Retry-After has passed.");
            }

            if (_next > now)
            {
                await Task.Delay(_next - now, time, ct);
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }

        return new Lease(this);
    }

    /// <summary>Holds every request for <paramref name="duration"/> (never shortening an earlier hold).</summary>
    public void Pause(TimeSpan duration)
    {
        var until = time.GetUtcNow() + duration;
        if (until > _pausedUntil)
        {
            _pausedUntil = until;
        }
    }

    private sealed class Lease(XemThrottle owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner._next = owner.UtcNow + owner._minInterval;
                owner._gate.Release();
            }
        }
    }
}
