using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Tags;

namespace Bigarr.Domain.Indexers;

/// <summary>A release source (blueprint §3.7). Torznab at the MVP; Newznab and usenet come with a usenet download client.</summary>
public sealed class Indexer
{
    /// <summary>Radarr's default movie categories: Movies and every sub-category (Foreign, Other, SD, HD, UHD, BluRay, 3D, DVD, WEB-DL).</summary>
    public static readonly int[] DefaultCategories = [2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060, 2070, 2080];

    public const int MinPriority = 1;

    public const int MaxPriority = 50;

    public int Id { get; set; }

    public required string Name { get; set; }

    public IndexerImplementation Implementation { get; set; } = IndexerImplementation.Torznab;

    /// <summary>Scheme and host, e.g. <c>http://prowlarr:9696/1</c>. Null for the mock.</summary>
    public string? BaseUrl { get; set; }

    public string ApiPath { get; set; } = "/api";

    public string? ApiKey { get; set; }

    public List<int> Categories { get; set; } = [.. DefaultCategories];

    /// <summary>1 is the highest priority, 50 the lowest; breaks ties between otherwise equal releases.</summary>
    public int Priority { get; set; } = 25;

    public bool EnableRss { get; set; } = true;

    public bool EnableAutomaticSearch { get; set; } = true;

    public bool EnableInteractiveSearch { get; set; } = true;

    /// <summary>Releases below this seeder count are rejected. Zero disables the check.</summary>
    public int MinimumSeeders { get; set; } = 1;

    public double? SeedRatio { get; set; }

    public int? SeedTimeMinutes { get; set; }

    /// <summary>Minimum interval between two requests to this indexer. Backoff on 429 and 5xx is on top of it.</summary>
    public int RateLimitSeconds { get; set; } = 2;

    public ICollection<Tag> Tags { get; set; } = [];

    /// <summary>Set when the row was imported from Prowlarr; name, URL, key, priority and enabled state then follow Prowlarr.</summary>
    public int? ProwlarrId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DownloadProtocol Protocol => DownloadProtocol.Torrent;
}
