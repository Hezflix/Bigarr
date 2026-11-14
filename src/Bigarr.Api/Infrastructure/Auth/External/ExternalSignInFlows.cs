using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Bigarr.Api.Infrastructure.Auth.External;

/// <summary>
/// A sign-in under way at a provider: to sign someone in (<c>LinkUserId</c> null) or to link the account to the user
/// who started it.
/// </summary>
public sealed record ExternalSignInFlow(string Id, string Provider, ExternalChallenge Challenge, int? LinkUserId)
{
    public DateTimeOffset ExpiresAt => Challenge.ExpiresAt;
}

/// <summary>
/// The sign-ins under way, in memory: a restart only loses sign-ins someone is still clicking through. Each has an
/// unguessable id that only the browser that started it holds (it also rides in a cookie), and it is removed as soon
/// as it ends. Capped, so starting sign-ins in a loop cannot grow it without bound.
/// </summary>
public sealed class ExternalSignInFlows(TimeProvider time)
{
    public const int MaxFlows = 200;

    private readonly ConcurrentDictionary<string, ExternalSignInFlow> _flows = new(StringComparer.Ordinal);

    public ExternalSignInFlow Start(string provider, ExternalChallenge challenge, int? linkUserId)
    {
        Purge();
        if (_flows.Count >= MaxFlows)
        {
            foreach (var oldest in _flows.Values.OrderBy(f => f.ExpiresAt).Take(_flows.Count - MaxFlows + 1))
            {
                _flows.TryRemove(oldest.Id, out _);
            }
        }

        var flow = new ExternalSignInFlow(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), provider, challenge, linkUserId);
        _flows[flow.Id] = flow;
        return flow;
    }

    /// <summary>The flow, expired or not (the caller says which); null when it never was or has already ended.</summary>
    public ExternalSignInFlow? Find(string id) => _flows.TryGetValue(id, out var flow) ? flow : null;

    public bool IsExpired(ExternalSignInFlow flow)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return time.GetUtcNow() >= flow.ExpiresAt;
    }

    public void End(string id) => _flows.TryRemove(id, out _);

    public int Count => _flows.Count;

    private void Purge()
    {
        // Kept a minute past expiry so a last poll still hears "expired" rather than "unknown".
        var cutoff = time.GetUtcNow() - TimeSpan.FromMinutes(1);
        foreach (var flow in _flows.Values.Where(f => f.ExpiresAt < cutoff))
        {
            _flows.TryRemove(flow.Id, out _);
        }
    }
}
