namespace Bigarr.Api.Features.Search;

/// <summary>Who asked for the search. The user bypasses monitoring and availability, the system does not (blueprint §5).</summary>
public enum SearchTrigger
{
    User = 1,

    /// <summary>Right after a film or version was added with search on add.</summary>
    Added = 2,

    /// <summary>Replacement for a download that failed.</summary>
    FailedDownload = 3,

    /// <summary>The cutoff-unmet job.</summary>
    CutoffUnmet = 4,
}

public enum AutomaticSearchOutcome
{
    Grabbed = 1,

    /// <summary>Releases came back, none passed for this version.</summary>
    NothingFound = 2,

    NoIndexer = 3,

    /// <summary>The version already has an active download, or does not exist any more.</summary>
    Skipped = 4,

    /// <summary>A release passed but no download client took it.</summary>
    Failed = 5,
}

public sealed record AutomaticSearchResult(
    int MovieId,
    string MovieTitle,
    int VersionId,
    string VersionName,
    AutomaticSearchOutcome Outcome,
    string? ReleaseTitle,
    int? GrabId,
    /// <summary>Why nothing was grabbed, in one line: the most frequent rejection, or the client's answer.</summary>
    string? Detail,
    int Releases,
    int Approved,
    List<IndexerErrorResponse> IndexerErrors);

public sealed record SearchRequest(int VersionId, SearchTrigger Trigger);

public enum WantedFilter
{
    /// <summary>Monitored, available, no file, no active download.</summary>
    Missing = 1,

    /// <summary>Has a file below the profile cutoff, upgrades allowed.</summary>
    CutoffUnmet = 2,
}

public sealed record WantedItemResponse(
    int MovieId,
    string MovieTitle,
    int? Year,
    string? PosterUrl,
    int VersionId,
    string VersionName,
    string QualityProfile,
    WantedFilter Kind,
    string? CurrentQuality,
    int? CurrentScore,
    string CutoffQuality,
    DateTime? LastSearchAt,
    int SearchAttempts,
    /// <summary>When the cutoff-unmet job may search it again; null means at its next pass.</summary>
    DateTime? NextSearchAt);

public sealed record WantedPageResponse(List<WantedItemResponse> Items, int Total, int Page, int PageSize);

public sealed record WantedSearchResponse(int Queued);
