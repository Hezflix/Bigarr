namespace Bigarr.Domain.Downloads;

public enum DownloadClientImplementation
{
    QBittorrent = 1,

    /// <summary>In-memory client that completes downloads on its own: development and tests, no network.</summary>
    Mock = 2,
}
