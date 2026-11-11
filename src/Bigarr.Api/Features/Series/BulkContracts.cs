namespace Bigarr.Api.Features.Series;

using Bigarr.Api.Features.Movies;
using Bigarr.Domain.Tv;

/// <param name="Versions">Which version of each series: <c>default</c> (when absent), <c>all</c> or <c>version:&lt;name&gt;</c>.</param>
/// <param name="SeriesType">A series-level setting: it applies to every selected series whatever <paramref name="Versions"/> picks.</param>
public sealed record SeriesBulkUpdateRequest(
    List<int> SeriesIds,
    string? Versions = null,
    bool? Monitored = null,
    int? QualityProfileId = null,
    NewItemMonitoring? MonitorNewItems = null,
    List<int>? TagIds = null,
    /// <summary>How <see cref="TagIds"/> combine with the version's tags; adds when absent.</summary>
    TagsMode? ApplyTags = null,
    SeriesType? SeriesType = null);

public sealed record SeriesBulkDeleteRequest(
    List<int> SeriesIds,
    /// <summary><c>all</c> deletes whole series.</summary>
    string? Versions = null,
    bool DeleteFiles = false,
    /// <summary>Answers what would happen and changes nothing: the confirmation reads it.</summary>
    bool DryRun = false);

/// <param name="Versions">Names of the versions acted on.</param>
/// <param name="Detail">Why the series was skipped or failed.</param>
/// <param name="Files">Episode files removed with the versions (delete with files).</param>
/// <param name="Promoted">The version that became the default because the default one was deleted.</param>
public sealed record BulkSeriesResult(int SeriesId, string Title, BulkOutcome Outcome, List<string> Versions, string? Detail = null, int Files = 0, string? Promoted = null);

public sealed record SeriesBulkResponse(List<BulkSeriesResult> Results);

/// <param name="RecycleBin">Where deleted files go; null when they are deleted for good.</param>
public sealed record SeriesBulkDeleteResponse(bool DryRun, string? RecycleBin, List<BulkSeriesResult> Results);
