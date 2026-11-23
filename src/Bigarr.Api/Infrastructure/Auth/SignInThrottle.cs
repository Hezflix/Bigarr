using System.Net;
using Microsoft.Extensions.Options;

namespace Bigarr.Api.Infrastructure.Auth;

/// <summary>
/// Slows down guessing: failed password sign-ins are counted per username and per client address over a fixed window,
/// and past the limit the next attempt has to wait until the oldest counted failure leaves the window. The address key
/// also counts the starts of an external sign-in (each one creates a PIN at the provider). The username is counted too
/// because the address can be forged by a client that reaches Bigarr directly (forwarded headers are trusted). The
/// counters live in memory: a restart forgets them. A limit of 0 turns that key off.
/// </summary>
public sealed class SignInThrottle(TimeProvider time, IOptions<SignInOptions> options)
{
    /// <summary>Most keys kept at once; past it a new key is not recorded rather than growing without bound.</summary>
    public const int MaxKeys = 10_000;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = new(StringComparer.Ordinal);

    /// <summary>How many keys are held right now.</summary>
    public int TrackedKeys
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count;
            }
        }
    }

    /// <summary>Null when the attempt may go on; otherwise how long until it may.</summary>
    public TimeSpan? RetryAfter(string? username, IPAddress? address)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var window = Window(settings);
        lock (_gate)
        {
            var byUsername = Wait(UsernameKey(username), settings.MaxFailuresPerUsername, now, window);
            var byAddress = Wait(AddressKey(address), settings.MaxFailuresPerAddress, now, window);
            return Longest(byUsername, byAddress);
        }
    }

    /// <summary>Records one failed sign-in under the username and under the address.</summary>
    public void Failed(string? username, IPAddress? address)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var window = Window(settings);
        lock (_gate)
        {
            Record(UsernameKey(username), settings.MaxFailuresPerUsername, now, window);
            Record(AddressKey(address), settings.MaxFailuresPerAddress, now, window);
        }
    }

    /// <summary>Forgets the username's failures; the address keeps its own.</summary>
    public void Succeeded(string? username)
    {
        if (UsernameKey(username) is { } key)
        {
            lock (_gate)
            {
                _failures.Remove(key);
            }
        }
    }

    /// <summary>
    /// Counts one start of an external sign-in under the address. Null when it may go on; otherwise how long the
    /// address has to wait (the start is then not counted).
    /// </summary>
    public TimeSpan? TryStart(IPAddress? address)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var window = Window(settings);
        lock (_gate)
        {
            var key = AddressKey(address);
            if (Wait(key, settings.MaxFailuresPerAddress, now, window) is { } wait)
            {
                return wait;
            }

            Record(key, settings.MaxFailuresPerAddress, now, window);
            return null;
        }
    }

    private static TimeSpan Window(SignInOptions settings) => TimeSpan.FromMinutes(Math.Max(1, settings.WindowMinutes));

    private static string? UsernameKey(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : "u:" + username.Trim().ToUpperInvariant();

    private static string? AddressKey(IPAddress? address) =>
        address is null ? null : "a:" + (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static TimeSpan? Longest(TimeSpan? a, TimeSpan? b) =>
        a is { } x && b is { } y ? (x > y ? x : y) : a ?? b;

    private TimeSpan? Wait(string? key, int limit, DateTimeOffset now, TimeSpan window)
    {
        if (key is null || limit <= 0 || !_failures.TryGetValue(key, out var queue))
        {
            return null;
        }

        Prune(queue, now, window);
        if (queue.Count == 0)
        {
            _failures.Remove(key);
            return null;
        }

        return queue.Count >= limit ? queue.Peek() + window - now : null;
    }

    private void Record(string? key, int limit, DateTimeOffset now, TimeSpan window)
    {
        if (key is null || limit <= 0)
        {
            return;
        }

        if (!_failures.TryGetValue(key, out var queue))
        {
            if (_failures.Count >= MaxKeys)
            {
                Sweep(now, window);
            }

            if (_failures.Count >= MaxKeys)
            {
                return;
            }

            queue = new Queue<DateTimeOffset>();
            _failures[key] = queue;
        }

        Prune(queue, now, window);
        queue.Enqueue(now);
    }

    private static void Prune(Queue<DateTimeOffset> queue, DateTimeOffset now, TimeSpan window)
    {
        while (queue.Count > 0 && queue.Peek() + window <= now)
        {
            queue.Dequeue();
        }
    }

    private void Sweep(DateTimeOffset now, TimeSpan window)
    {
        foreach (var (key, queue) in _failures.ToList())
        {
            Prune(queue, now, window);
            if (queue.Count == 0)
            {
                _failures.Remove(key);
            }
        }
    }
}
