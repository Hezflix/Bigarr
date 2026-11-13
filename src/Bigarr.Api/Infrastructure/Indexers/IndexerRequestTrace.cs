using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Infrastructure.Indexers;

/// <summary>
/// One request Bigarr sent an indexer, or would have sent: <see cref="Skipped"/> when the indexer was paused by its
/// backoff. <see cref="Url"/> is redacted (<see cref="IndexerRequestTrace.Redact"/>) before it is stored, so a secret
/// never leaves the server through a trace.
/// </summary>
public sealed record IndexerRequestRecord(
    int IndexerId,
    string Indexer,
    /// <summary>Where the request sits in the search: <c>IDs</c>, <c>Fallback: titles</c>, <c>Season 3 (series fallback) › IDs</c>…</summary>
    string Step,
    /// <summary>The Torznab/Newznab function: <c>search</c>, <c>tvsearch</c>, <c>movie</c>, <c>caps</c>; empty when skipped.</summary>
    string Function,
    /// <summary>The query parameters asked (<c>q</c>, ids, <c>season</c>, <c>ep</c>, <c>cat</c>…), never the API key.</summary>
    IReadOnlyDictionary<string, string> Parameters,
    string? Url,
    int? StatusCode,
    long DurationMs,
    /// <summary>Time spent waiting for the indexer's rate limit before the request went out.</summary>
    long WaitedMs,
    /// <summary>Releases (or nothing, for <c>caps</c>) the answer held; null when it failed.</summary>
    int? Results,
    IndexerFailureKind? ErrorKind,
    string? Error,
    bool Skipped = false);

/// <summary>
/// Records the indexer requests of one interactive search, for its advanced view. Ambient (an <see cref="AsyncLocal{T}"/>)
/// so the protocol clients and the gateway record without a new parameter on every method: a search that did not ask for
/// it has no trace and every call below is a no-op. Steps nest the same way: the search names its phase (a fallback
/// season), the criteria their numbering (XEM), the client its tier (ids, then titles).
/// </summary>
public sealed partial class IndexerRequestTrace
{
    public const string Mask = "•••";

    private static readonly AsyncLocal<IndexerRequestTrace?> CurrentTrace = new();
    private static readonly AsyncLocal<string?> CurrentStep = new();

    private readonly Lock _lock = new();
    private readonly List<IndexerRequestRecord> _records = [];
    private readonly ConcurrentDictionary<int, long> _waits = new();

    /// <summary>The trace of the search running in this flow; null when none was asked.</summary>
    public static IndexerRequestTrace? Current => CurrentTrace.Value;

    /// <summary>The step of the request about to go out, nested steps joined by <c>›</c>.</summary>
    public static string CurrentStepLabel => CurrentStep.Value ?? string.Empty;

    /// <summary>
    /// Starts recording for the calling async method and everything it awaits; ends with that method. Null (and nothing
    /// recorded) when not <paramref name="enabled"/>.
    /// </summary>
    public static IndexerRequestTrace? Start(bool enabled)
    {
        if (!enabled)
        {
            return null;
        }

        var trace = new IndexerRequestTrace();
        CurrentTrace.Value = trace;
        CurrentStep.Value = null;
        return trace;
    }

    /// <summary>Names a step for the requests sent until the scope is disposed. A no-op without a trace or a label.</summary>
    public static StepScope Step(string? label)
    {
        if (CurrentTrace.Value is null || string.IsNullOrWhiteSpace(label))
        {
            return default;
        }

        var previous = CurrentStep.Value;
        CurrentStep.Value = previous is null ? label : $"{previous} › {label}";
        return new StepScope(previous, true);
    }

    /// <summary>The gateway waited for the indexer's rate limit: added to its next request.</summary>
    public static void Waited(int indexerId, TimeSpan wait)
    {
        if (CurrentTrace.Value is { } trace && wait > TimeSpan.Zero)
        {
            trace._waits.AddOrUpdate(indexerId, (long)wait.TotalMilliseconds, (_, sum) => sum + (long)wait.TotalMilliseconds);
        }
    }

    /// <summary>One request, as it went: the URL is redacted here whatever the caller passed.</summary>
    public static void Record(
        Indexer indexer,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<int>? categories,
        string? url,
        int? statusCode,
        TimeSpan duration,
        int? results,
        IndexerException? error)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(parameters);
        if (CurrentTrace.Value is not { } trace)
        {
            return;
        }

        var shown = parameters.Where(p => !IsSecret(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (categories is { Count: > 0 })
        {
            shown["cat"] = string.Join(',', categories);
        }

        shown.Remove("t", out var function);
        trace.Add(new IndexerRequestRecord(
            indexer.Id,
            indexer.Name,
            CurrentStepLabel,
            function ?? string.Empty,
            shown,
            url is null ? null : Redact(url),
            statusCode,
            (long)duration.TotalMilliseconds,
            trace._waits.TryRemove(indexer.Id, out var waited) ? waited : 0,
            error is null ? results : null,
            error?.Kind,
            error is null ? null : Redact(error.Message)));
    }

    /// <summary>The indexer was not asked: its backoff pauses it.</summary>
    public static void RecordSkipped(Indexer indexer, IndexerException reason)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(reason);
        CurrentTrace.Value?.Add(new IndexerRequestRecord(
            indexer.Id, indexer.Name, CurrentStepLabel, string.Empty, new Dictionary<string, string>(), null, null, 0, 0, null, reason.Kind, Redact(reason.Message), Skipped: true));
    }

    /// <summary>The requests in the order they were sent.</summary>
    public IReadOnlyList<IndexerRequestRecord> Records
    {
        get
        {
            lock (_lock)
            {
                return [.. _records];
            }
        }
    }

    /// <summary>
    /// The URL with every secret masked: the API key and any parameter named like a credential (passkey, token, auth…),
    /// and a user:password before the host. For the logs and for anything sent to the browser.
    /// </summary>
    public static string Redact(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var masked = SecretParameterRegex().Replace(url, m => $"{m.Groups["key"].Value}={Mask}");
        return UserInfoRegex().Replace(masked, $"://{Mask}@");
    }

    private static bool IsSecret(string parameter) => SecretNameRegex().IsMatch(parameter);

    private void Add(IndexerRequestRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
        }
    }

    public readonly struct StepScope(string? previous, bool active) : IDisposable
    {
        public void Dispose()
        {
            if (active)
            {
                CurrentStep.Value = previous;
            }
        }
    }

    private const string SecretNames = "api[_-]?key|apikey|passkey|pass|password|passwd|token|access[_-]?token|auth|authkey|secret|sid|session|rsskey|torrent_pass|key";

    [GeneratedRegex("^(" + SecretNames + ")$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretNameRegex();

    [GeneratedRegex(@"(?<=[?&;])(?<key>" + SecretNames + @")=[^&#;]*", RegexOptions.IgnoreCase)]
    private static partial Regex SecretParameterRegex();

    [GeneratedRegex(@"://[^/?#@\s]+@")]
    private static partial Regex UserInfoRegex();
}
