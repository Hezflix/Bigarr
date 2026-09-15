namespace Bigarr.Domain.Downloads;

public enum DownloadClientFailureKind
{
    /// <summary>Connection refused, DNS failure, timeout.</summary>
    Unreachable = 1,

    /// <summary>Wrong credentials, banned IP, session refused.</summary>
    Authentication = 2,

    /// <summary>The client answered with an error or with something that is not its API.</summary>
    Request = 3,

    /// <summary>The release could not be turned into something the client accepts: no link, unreadable torrent file.</summary>
    InvalidRelease = 4,
}

public sealed class DownloadClientException(DownloadClientFailureKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public DownloadClientFailureKind Kind { get; } = kind;
}
