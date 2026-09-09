namespace Bigarr.Domain.Indexers;

public enum IndexerFailureKind
{
    /// <summary>Network error or timeout.</summary>
    Unreachable = 1,

    /// <summary>HTTP 401/403 or a Torznab credentials error.</summary>
    Authentication = 2,

    /// <summary>HTTP 429 or a Torznab request-limit error: back off.</summary>
    RateLimited = 3,

    /// <summary>HTTP 5xx.</summary>
    ServerError = 4,

    /// <summary>The response was not what a Torznab endpoint returns.</summary>
    InvalidResponse = 5,

    /// <summary>Any other Torznab error element.</summary>
    Request = 6,

    /// <summary>The indexer is in backoff after earlier failures; nothing was sent.</summary>
    Disabled = 7,
}

public sealed class IndexerException(IndexerFailureKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public IndexerFailureKind Kind { get; } = kind;
}
