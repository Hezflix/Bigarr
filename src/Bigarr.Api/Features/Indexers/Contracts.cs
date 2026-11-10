using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Indexers;

/// <summary>Shared by create and update: an indexer is small enough to be sent whole.</summary>
public sealed record IndexerRequest(
    string Name,
    IndexerImplementation Implementation,
    string? BaseUrl = null,
    string? ApiPath = null,
    string? ApiKey = null,
    List<int>? Categories = null,
    int Priority = 25,
    bool EnableRss = true,
    bool EnableAutomaticSearch = true,
    bool EnableInteractiveSearch = true,
    int MinimumSeeders = 1,
    double? SeedRatio = null,
    int? SeedTimeMinutes = null,
    /// <summary>Null keeps the current value (2 seconds on a new indexer): Prowlarr does not send it.</summary>
    int? RateLimitSeconds = null,
    /// <summary>Set when Prowlarr pushes the indexer through the compat API, from the <c>/{id}/</c> in its URL; null keeps the current link.</summary>
    int? ProwlarrId = null,
    /// <summary>Series categories; null keeps the current ones (Prowlarr does not send them), empty means "from the capabilities".</summary>
    List<int>? TvCategories = null,
    /// <summary>Anime categories; null keeps the current ones, empty means "from the capabilities".</summary>
    List<int>? AnimeCategories = null);

public sealed record IndexerStatusResponse(
    bool Healthy,
    int Failures,
    DateTime? DisabledUntil,
    string? LastError,
    DateTime? LastFailureAt,
    DateTime? LastSuccessAt);

public sealed record IndexerResponse(
    int Id,
    string Name,
    IndexerImplementation Implementation,
    string? BaseUrl,
    string ApiPath,
    string? ApiKey,
    List<int> Categories,
    int Priority,
    bool EnableRss,
    bool EnableAutomaticSearch,
    bool EnableInteractiveSearch,
    int MinimumSeeders,
    double? SeedRatio,
    int? SeedTimeMinutes,
    int RateLimitSeconds,
    DownloadProtocol Protocol,
    /// <summary>Set when Prowlarr manages the row: each sync rewrites everything but the rate limit, and the row goes when Prowlarr removes it.</summary>
    int? ProwlarrId,
    IndexerStatusResponse Status,
    DateTime CreatedAt,
    /// <summary>Empty until filled from the capabilities (at the first series search or test).</summary>
    List<int>? TvCategories = null,
    List<int>? AnimeCategories = null);

public sealed record IndexerTestResponse(
    bool SupportsMovieSearch,
    bool SupportsImdbSearch,
    bool SupportsTmdbSearch,
    int CategoryCount,
    /// <summary>Releases returned by a feed fetch; zero on a working but empty indexer.</summary>
    int RecentReleases,
    bool SupportsTvSearch = false,
    /// <summary>The <c>tv-search</c> parameters the indexer takes (<c>q</c>, <c>season</c>, <c>ep</c>, <c>tvdbid</c>…).</summary>
    List<string>? TvSearchParams = null,
    /// <summary>The TV and anime categories the capabilities list: what an empty row field falls back to.</summary>
    List<int>? TvCategories = null,
    List<int>? AnimeCategories = null);
