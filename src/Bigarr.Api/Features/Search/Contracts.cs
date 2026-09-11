using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Search;

public sealed record RejectionResponse(string Reason, bool Temporary);

/// <summary>The verdict of one version on one release. A release matched to the film has one per version searched.</summary>
public sealed record VersionDecisionResponse(int VersionId, string VersionName, bool Approved, List<RejectionResponse> Rejections, int CustomFormatScore = 0);

public sealed record ReleaseResponse(
    string Guid,
    string Title,
    int IndexerId,
    string Indexer,
    int IndexerPriority,
    DownloadProtocol Protocol,
    long Size,
    int? Seeders,
    int? Leechers,
    DateTimeOffset PublishDate,
    string? DownloadUrl,
    string? MagnetUrl,
    string? InfoUrl,
    string? InfoHash,
    IndexerFlags Flags,
    int QualityId,
    string Quality,
    int Resolution,
    int RevisionVersion,
    bool IsRepack,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    /// <summary>Custom formats matched; the score depends on each version's profile and sits on the verdict.</summary>
    List<string> CustomFormats,
    string? ParsedTitle,
    int? ParsedYear,
    /// <summary>False when the release is not this film at all; then <see cref="Versions"/> is empty.</summary>
    bool MatchesMovie,
    /// <summary>Why the release is not the film, when it is not.</summary>
    List<RejectionResponse> Rejections,
    List<VersionDecisionResponse> Versions);

public sealed record IndexerErrorResponse(int IndexerId, string Indexer, IndexerFailureKind Kind, string Message);

public sealed record SearchResponse(
    int MovieId,
    DateTime SearchedAt,
    int IndexersQueried,
    List<ReleaseResponse> Releases,
    List<IndexerErrorResponse> IndexerErrors);
