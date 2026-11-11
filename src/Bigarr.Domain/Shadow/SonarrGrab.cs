namespace Bigarr.Domain.Shadow;

/// <summary>
/// A release a followed Sonarr grabbed, read from its history: the other side of the comparison with Bigarr's
/// <see cref="SeriesShadowPick"/>. Sonarr writes one history line per episode of a grab (a season pack writes one per
/// episode, all with the same download id): the lines of one grab become one row with one <see cref="SonarrGrabEpisode"/>
/// per episode. Keyed on the first line's history id, so reading the same lines again adds nothing. The series is
/// snapshotted (TVDB id, title); <see cref="SeriesId"/> and <see cref="VersionId"/> are filled once the series is in Bigarr.
/// </summary>
public sealed class SonarrGrab
{
    public int Id { get; set; }

    public int FollowedSonarrId { get; set; }

    public FollowedSonarr FollowedSonarr { get; set; } = null!;

    /// <summary>The smallest history id among the grab's lines.</summary>
    public int SonarrHistoryId { get; set; }

    public int SonarrSeriesId { get; set; }

    public int TvdbId { get; set; }

    public int? SeriesId { get; set; }

    public int? VersionId { get; set; }

    public required string SeriesTitle { get; set; }

    public required string VersionName { get; set; }

    public DateTime GrabbedAt { get; set; }

    public required string ReleaseTitle { get; set; }

    public string? ReleaseGuid { get; set; }

    /// <summary>Sonarr's <c>torrentInfoHash</c>, lower case like Bigarr's picks.</summary>
    public string? InfoHash { get; set; }

    /// <summary>The download client's id: later import and failure lines carry it.</summary>
    public string? DownloadId { get; set; }

    public string? IndexerName { get; set; }

    public long Size { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>Sonarr's <c>releaseSource</c>: <c>Rss</c>, <c>Search</c>, <c>UserInvokedSearch</c>, <c>InteractiveSearch</c>, <c>ReleasePush</c>.</summary>
    public string? ReleaseSource { get; set; }

    /// <summary>Sonarr's <c>releaseType</c>: <c>SingleEpisode</c>, <c>MultiEpisode</c>, <c>SeasonPack</c>.</summary>
    public string? ReleaseType { get; set; }

    public string? DownloadClient { get; set; }

    /// <summary>The quality name, in Bigarr's (Radarr's) naming.</summary>
    public string? Quality { get; set; }

    /// <summary>The quality id in Bigarr's numbering (Sonarr's ids translated).</summary>
    public int? QualityId { get; set; }

    public int? RevisionVersion { get; set; }

    public string? ReleaseGroup { get; set; }

    public List<string> Languages { get; set; } = [];

    public List<string> CustomFormats { get; set; } = [];

    /// <summary>Sonarr's own score of the release when it grabbed it.</summary>
    public int? CustomFormatScore { get; set; }

    /// <summary>A season (or multi-season) pack, as Sonarr's release type says.</summary>
    public bool FullSeason { get; set; }

    public RadarrGrabState State { get; set; } = RadarrGrabState.Grabbed;

    public DateTime? ResolvedAt { get; set; }

    /// <summary>Bigarr's engine on the same release for the same version, when the grab was read.</summary>
    public BigarrVerdict? BigarrVerdict { get; set; }

    public ICollection<SonarrGrabEpisode> Episodes { get; set; } = [];
}

/// <summary>One episode a Sonarr grab covers, in Sonarr's (TVDB) numbering, with Bigarr's episode once it exists.</summary>
public sealed class SonarrGrabEpisode
{
    public int Id { get; set; }

    public int GrabId { get; set; }

    public SonarrGrab Grab { get; set; } = null!;

    public int SonarrHistoryId { get; set; }

    public int SonarrEpisodeId { get; set; }

    /// <summary>Bigarr's shared episode, once the series is in Bigarr; null after its deletion.</summary>
    public int? EpisodeId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public int? AbsoluteEpisodeNumber { get; set; }
}
