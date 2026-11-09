namespace Bigarr.Api.Infrastructure.Metadata.Tvdb;

/// <summary>
/// The bearer token of the TVDB session. A token lives a month; it is kept for 25 days, and dropped as soon as TVDB
/// answers 401. Singleton: one login per process, not one per request.
/// </summary>
public sealed class TvdbTokenCache(TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(25);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(Func<CancellationToken, Task<string>> login, CancellationToken cancellationToken)
    {
        if (Current is { } token)
        {
            return token;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current is { } fresh)
            {
                return fresh;
            }

            var created = await login(cancellationToken);
            _token = created;
            _expiresAt = time.GetUtcNow() + Lifetime;
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets <paramref name="token"/> unless another request already replaced it.</summary>
    public void Invalidate(string token)
    {
        if (_token == token)
        {
            _token = null;
        }
    }

    private string? Current => _token is not null && time.GetUtcNow() < _expiresAt ? _token : null;
}
