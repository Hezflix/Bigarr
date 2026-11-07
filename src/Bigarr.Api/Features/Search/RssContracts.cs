using Bigarr.Domain.Acquisition;

namespace Bigarr.Api.Features.Search;

/// <summary>Bound from <c>Bigarr:Rss</c>. Radarr's defaults: every 15 minutes, never below 10.</summary>
public sealed class RssSyncOptions
{
    public const string Section = "Bigarr:Rss";

    public const int MinimumIntervalMinutes = 10;

    public int IntervalMinutes { get; set; } = 15;

    /// <summary>Grace period after startup before the first pass, so a restart does not hammer the indexers.</summary>
    public int InitialDelaySeconds { get; set; } = 60;

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(MinimumIntervalMinutes, IntervalMinutes));
}

public sealed record RssMovieRef(int Id, string Title, int? Year, string? PosterUrl);

/// <summary>One release seen in a feed, with what the library made of it.</summary>
public sealed record RssReleaseResponse(
    string Guid,
    string Title,
    int IndexerId,
    string Indexer,
    long Size,
    int? Seeders,
    int? Leechers,
    DateTimeOffset PublishDate,
    DateTime SeenAt,
    string? InfoUrl,
    IndexerFlags Flags,
    int QualityId,
    string Quality,
    int RevisionVersion,
    bool IsRepack,
    string? Edition,
    string? ReleaseGroup,
    List<string> Languages,
    List<string> CustomFormats,
    string? ParsedTitle,
    int? ParsedYear,
    /// <summary>The film of the library this release belongs to, or null with <see cref="UnmatchedReason"/>.</summary>
    RssMovieRef? Movie,
    string? UnmatchedReason,
    /// <summary>One verdict per version of the matched film (automatic rules: monitoring and availability count).</summary>
    List<VersionDecisionResponse> Versions,
    /// <summary>The single version the release would be grabbed for, when at least one accepts it (blueprint §3.3).</summary>
    int? AttributedVersionId,
    string? AttributedVersionName,
    /// <summary>Set when this pass sent the release to a download client.</summary>
    int? GrabId = null,
    /// <summary>Why the attributed release was not grabbed after all: another release of the pass was better, or the client refused it.</summary>
    string? GrabError = null,
    /// <summary>Shadow mode: the pick recorded in place of a grab.</summary>
    int? ShadowPickId = null);

public sealed record RssIndexerStatusResponse(int IndexerId, string Indexer, DateTime? LastRunAt, string? LastError, int LastFetched, int LastNew);

public sealed record RssStatusResponse(
    /// <summary><c>Bigarr:Rss:Enabled</c>: the pass is one of the scheduled tasks.</summary>
    bool Enabled,
    /// <summary><c>Bigarr:Scheduler:Enabled</c>: when false no task runs on its own, so the pass only runs when triggered.</summary>
    bool SchedulerEnabled,
    int IntervalMinutes,
    bool Running,
    DateTime? LastRunAt,
    /// <summary>Set only when the pass runs on its own: RSS and the scheduler both enabled.</summary>
    DateTime? NextRunAt,
    int Stored,
    int Matched,
    int WouldGrab,
    List<RssIndexerStatusResponse> Indexers,
    int Grabbed = 0,
    /// <summary>Shadow mode: releases recorded as picks instead of grabs.</summary>
    int Picked = 0);

public sealed record RssSyncResponse(int Indexers, int Fetched, int New, int Matched, int WouldGrab, List<IndexerErrorResponse> IndexerErrors, int Grabbed = 0, int Picked = 0);
