using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Indexers;

namespace Bigarr.Api.Features.Search;

public sealed record SeriesEpisodeRef(int Id, int SeasonNumber, int EpisodeNumber, int? AbsoluteEpisodeNumber, string Title);

/// <summary>One series version's verdict on a release, over every episode the release covers.</summary>
public sealed record SeriesVersionDecisionResponse(int VersionId, string VersionName, bool Approved, List<RejectionResponse> Rejections, int CustomFormatScore, List<int> TargetIds);

public sealed record SeriesReleaseResponse(
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
    string? ReleaseGroup,
    List<string> Languages,
    List<string> CustomFormats,
    string? ParsedSeriesTitle,
    /// <summary>False when the release names another show (title or TVDB id); still judged, and can be grabbed by hand.</summary>
    bool MatchesSeries,
    /// <summary>Why it is not this show, or why it maps to no episode of it.</summary>
    List<RejectionResponse> Rejections,
    /// <summary>The episodes it covers, in TVDB numbering; empty when it maps to none.</summary>
    List<SeriesEpisodeRef> Episodes,
    /// <summary><c>S01E05</c>, <c>S01E01–S01E10 (10 episodes)</c>…</summary>
    string? EpisodesLabel,
    /// <summary>A season pack (or multi-season pack).</summary>
    bool FullSeason,
    List<SeriesVersionDecisionResponse> Versions,
    int? SuggestedVersionId,
    /// <summary>Which numbering found the episodes: <c>tvdb</c>, <c>xem</c>, <c>order</c> (the series' order) or <c>absolute</c>.</summary>
    Bigarr.Domain.Tv.EpisodeNumbering? Numbering = null,
    /// <summary>A fallback hit on the series' order nothing confirms: held for a manual choice, never grabbed automatically.</summary>
    bool Held = false);

public sealed record SeriesSearchResponse(
    int SeriesId,
    /// <summary><c>episode</c>, <c>season</c> or <c>series</c>.</summary>
    string Scope,
    int? SeasonNumber,
    int? EpisodeId,
    /// <summary>What was searched, for the page: <c>S01E05</c>, <c>Season 1</c>, the series title.</summary>
    string Label,
    DateTime SearchedAt,
    int IndexersQueried,
    List<SeriesReleaseResponse> Releases,
    List<IndexerErrorResponse> IndexerErrors,
    string? Query = null,
    /// <summary>A whole-series search: the seasons the request by ids left uncovered, then searched one by one.</summary>
    List<int>? FallbackSeasons = null,
    /// <summary>TVDB numbering found no acceptable release: the series' order was asked too.</summary>
    bool OrderFallback = false,
    /// <summary>What each indexer was asked (<c>includeQueries=true</c> only), URLs redacted.</summary>
    List<IndexerQueryReport>? Queries = null);
