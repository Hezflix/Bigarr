using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Search;

public sealed record RejectionResponse(string Reason, bool Temporary);

/// <summary>The verdict of one version on one release. A release whose title parses has one per version searched.</summary>
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
    /// <summary>False when the release does not look like this film; it is still judged by every version and can be grabbed by hand.</summary>
    bool MatchesMovie,
    /// <summary>Why the release is not the film, when it is not.</summary>
    List<RejectionResponse> Rejections,
    /// <summary>One verdict per version; empty only when the title does not parse.</summary>
    List<VersionDecisionResponse> Versions,
    /// <summary>
    /// Where a grab goes unless the user picks another version: the version that accepts the release (the RSS
    /// attribution rule), otherwise the one closest to accepting it, otherwise the default version.
    /// </summary>
    int? SuggestedVersionId = null,
    /// <summary>What the film's versions already did with this release (grabbed, imported, on disk, picked in shadow mode); at most one per version.</summary>
    List<ReleasePickResponse>? Picks = null);

/// <summary>Where a release stands for one version of the film.</summary>
public enum ReleasePickKind
{
    /// <summary>Grabbed and not imported yet: in the download client or waiting for import.</summary>
    Downloading,

    /// <summary>The version's file came from this release.</summary>
    OnDisk,

    /// <summary>Grabbed and imported, but the file was replaced since.</summary>
    Imported,

    /// <summary>Grabbed, then the download or the import failed, or it left the client.</summary>
    Failed,

    /// <summary>Grabbed, then dismissed by the user.</summary>
    Grabbed,

    /// <summary>Shadow mode: Bigarr's pick, nothing sent to a client.</summary>
    Shadow,
}

public sealed record ReleasePickResponse(int VersionId, string VersionName, ReleasePickKind Kind, DateTime At);

public sealed record IndexerErrorResponse(int IndexerId, string Indexer, IndexerFailureKind Kind, string Message);

public sealed record SearchResponse(
    int MovieId,
    DateTime SearchedAt,
    int IndexersQueried,
    List<ReleaseResponse> Releases,
    List<IndexerErrorResponse> IndexerErrors,
    /// <summary>The text the indexers were asked, when the user typed one; null when the film was searched by its ids and titles.</summary>
    string? Query = null,
    /// <summary>What each indexer was asked (<c>includeQueries=true</c> only), URLs redacted.</summary>
    List<IndexerQueryReport>? Queries = null);
