using Bigarr.Domain.Acquisition;

namespace Bigarr.Domain.Downloads;

/// <summary>Where grabs are sent: a qBittorrent instance and the category Bigarr owns in it.</summary>
public sealed class DownloadClient
{
    public const int MinPriority = 1;

    public const int MaxPriority = 50;

    public const string DefaultCategory = "bigarr-movies";

    /// <summary>Sonarr's is <c>tv-sonarr</c>; Bigarr's own name keeps a Sonarr sharing the client out of its way.</summary>
    public const string DefaultSeriesCategory = "bigarr-tv";

    public int Id { get; set; }

    public required string Name { get; set; }

    public DownloadClientImplementation Implementation { get; set; } = DownloadClientImplementation.QBittorrent;

    /// <summary>A disabled client keeps its configuration and its grabs but receives nothing new.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Host name or address of the WebUI. Null for the mock.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 8080;

    public bool UseSsl { get; set; }

    /// <summary>Path prefix when the WebUI sits behind a reverse proxy, e.g. <c>/qbittorrent</c>. Null at the root.</summary>
    public string? UrlBase { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Every torrent Bigarr adds carries this category; the queue is the category's content.</summary>
    public string Category { get; set; } = DefaultCategory;

    /// <summary>Series grabs carry this one (Sonarr's <c>tv-sonarr</c>): the import knows a download of it is episodes.</summary>
    public string SeriesCategory { get; set; } = DefaultSeriesCategory;

    /// <summary>The category a grab of this kind is sent with.</summary>
    public string CategoryFor(bool series) => series ? SeriesCategory : Category;

    /// <summary>True when <paramref name="category"/> is the series one (and not also the film one).</summary>
    public bool IsSeriesCategory(string? category) =>
        category is not null && string.Equals(category, SeriesCategory, StringComparison.Ordinal) && !string.Equals(category, Category, StringComparison.Ordinal);

    /// <summary>Add torrents stopped, to be started by hand in the client.</summary>
    public bool AddPaused { get; set; }

    /// <summary>1 is tried first when several clients are enabled.</summary>
    public int Priority { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DownloadProtocol Protocol => DownloadProtocol.Torrent;

    /// <summary>Scheme, host, port and URL base, without a trailing slash.</summary>
    public string BaseUrl
    {
        get
        {
            var scheme = UseSsl ? "https" : "http";
            var path = string.IsNullOrWhiteSpace(UrlBase) ? string.Empty : "/" + UrlBase.Trim().Trim('/');
            return $"{scheme}://{Host}:{Port}{path}";
        }
    }
}
