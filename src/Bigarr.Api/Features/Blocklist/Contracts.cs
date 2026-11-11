namespace Bigarr.Api.Features.Blocklist;

public sealed record BlocklistEntryResponse(
    int Id,
    int VersionId,
    string? VersionName,
    int? MovieId,
    string? MovieTitle,
    int? MovieYear,
    string? PosterUrl,
    string SourceTitle,
    string? Quality,
    string? ReleaseGuid,
    string? InfoHash,
    int? IndexerId,
    string? IndexerName,
    string? Reason,
    DateTime CreatedAt)
{
    /// <summary>A series entry: the show, its version and the episode the release was blocked for.</summary>
    public int? SeriesId { get; init; }

    public string? SeriesTitle { get; init; }

    /// <summary><c>S01E05</c>.</summary>
    public string? Episode { get; init; }
}

public sealed record BlocklistPageResponse(List<BlocklistEntryResponse> Items, int Total, int Page, int PageSize);

public sealed record BlocklistBulkDeleteRequest(List<int> Ids);
