namespace Bigarr.Api.Infrastructure.Xem;

/// <summary>
/// What is remembered about TheXEM between calls (a singleton): the list of TVDB ids it has a mapping for, when the
/// scene names were last read, and whether the last slow pass was cut short. The list and the names are read at most
/// once per <see cref="XemOptions.MapMaxAgeHours"/>.
/// </summary>
public sealed class XemMappedIds(TimeProvider time)
{
    /// <summary>After a failed read of the list, callers that do not insist leave TheXEM alone for this long.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<int>? _ids;
    private DateTimeOffset _fetchedAt;
    private DateTimeOffset _nextTry = DateTimeOffset.MinValue;
    private DateTimeOffset? _namesFetchedAt;

    /// <summary>
    /// Set when the last slow pass was cut short by TheXEM (rate limit, unreachable), cleared when a pass completes.
    /// Read by the health check.
    /// </summary>
    public (DateTimeOffset At, string Message)? LastFailure { get; private set; }

    public void Failed(string message) => LastFailure = (time.GetUtcNow(), message);

    public void Answered() => LastFailure = null;

    public bool NamesAreFresh(TimeSpan lifetime) => _namesFetchedAt is { } at && time.GetUtcNow() - at < lifetime;

    public void NamesFetched() => _namesFetchedAt = time.GetUtcNow();

    /// <summary>
    /// The mapped ids, read again once older than <paramref name="lifetime"/>. Null when the list was never read and
    /// cannot be now: callers then ask for the series' mapping directly. With <paramref name="throwOnFailure"/> (the
    /// slow pass, which needs the list) a failure is thrown instead of being logged, and no back-off applies.
    /// </summary>
    public async Task<HashSet<int>?> GetAsync(IXemClient client, ILogger logger, TimeSpan lifetime, CancellationToken ct, bool throwOnFailure = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_ids is not null && now - _fetchedAt < lifetime)
            {
                return _ids;
            }

            if (!throwOnFailure && now < _nextTry)
            {
                return _ids;
            }

            try
            {
                _ids = [.. await client.GetMappedTvdbIdsAsync(ct)];
                _fetchedAt = time.GetUtcNow();
            }
            catch (Exception ex) when (XemService.IsUnreachable(ex, ct))
            {
                _nextTry = time.GetUtcNow() + FailureBackoff;
                if (throwOnFailure)
                {
                    throw;
                }

                logger.LogWarning("TheXEM list of mapped series unavailable, not asked again for {Minutes} min: {Message}", FailureBackoff.TotalMinutes, ex.Message);
            }

            return _ids;
        }
        finally
        {
            _gate.Release();
        }
    }
}
