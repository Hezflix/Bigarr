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
    int RateLimitSeconds = 2,
    /// <summary>Set by the Prowlarr paths (read-only sync, or Prowlarr pushing through the compat API) so both recognise the row.</summary>
    int? ProwlarrId = null);

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
    /// <summary>Set when the row comes from Prowlarr; name, URL, key, priority and enabled state follow it on each sync.</summary>
    int? ProwlarrId,
    IndexerStatusResponse Status,
    DateTime CreatedAt);

public sealed record IndexerTestResponse(
    bool SupportsMovieSearch,
    bool SupportsImdbSearch,
    bool SupportsTmdbSearch,
    int CategoryCount,
    /// <summary>Releases returned by a feed fetch; zero on a working but empty indexer.</summary>
    int RecentReleases);
