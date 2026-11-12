using System.Text.Json.Serialization;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Notifications.Radarr;

namespace Bigarr.Api.Features.Notifications.Sonarr;

/// <summary>
/// Sonarr's webhook bodies (v4, the <c>main</c> branch's <c>NzbDrone.Core/Notifications/Webhook</c>), one record per payload class, same member
/// names, written like the Radarr ones (<see cref="RadarrCompat.Json"/>: camelCase, nulls left out, enums camelCase,
/// <c>eventType</c> in Sonarr's PascalCase). Sonarr's language and image objects have Radarr's shape.
/// </summary>
public abstract record SonarrWebhookPayload
{
    [JsonPropertyOrder(-3)]
    public required string EventType { get; init; }

    [JsonPropertyOrder(-2)]
    public string? InstanceName { get; init; }

    [JsonPropertyOrder(-1)]
    public string? ApplicationUrl { get; init; }
}

/// <param name="Id">The compat series id, i.e. the series version's id (one Sonarr series per version).</param>
/// <param name="Type">Sonarr's <c>SeriesTypes</c>: <c>standard</c>, <c>daily</c>, <c>anime</c>.</param>
/// <param name="Tags">Tag labels, as Sonarr sends them.</param>
public sealed record SonarrWebhookSeries(
    int Id,
    string Title,
    string? TitleSlug,
    string? Path,
    int TvdbId,
    int TvMazeId,
    int TmdbId,
    string? ImdbId,
    string Type,
    int Year,
    List<string>? Genres,
    List<RadarrImage>? Images,
    List<string>? Tags,
    RadarrLanguage? OriginalLanguage);

/// <param name="AirDate">Local air date, <c>yyyy-MM-dd</c>.</param>
public sealed record SonarrWebhookEpisode(
    int Id,
    int EpisodeNumber,
    int SeasonNumber,
    string? Title,
    string? Overview,
    string? AirDate,
    DateTime? AirDateUtc,
    int SeriesId,
    int TvdbId);

/// <summary>
/// Sonarr's <c>WebhookEpisodeFile</c>, and its <c>WebhookRenamedEpisodeFile</c> subclass through the two
/// <c>Previous*</c> members.
/// </summary>
public sealed record SonarrWebhookEpisodeFile
{
    public int Id { get; init; }

    public string? RelativePath { get; init; }

    public string? Path { get; init; }

    public string? Quality { get; init; }

    public int QualityVersion { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? SceneName { get; init; }

    public long Size { get; init; }

    public DateTime? DateAdded { get; init; }

    public List<RadarrLanguage>? Languages { get; init; }

    public RadarrWebhookMediaInfo? MediaInfo { get; init; }

    public string? SourcePath { get; init; }

    public string? RecycleBinPath { get; init; }

    public string? PreviousRelativePath { get; init; }

    public string? PreviousPath { get; init; }
}

/// <summary>What Sonarr sends on a grab: the parsed release as it was decided.</summary>
public sealed record SonarrWebhookRelease(
    string? Quality,
    int QualityVersion,
    string? ReleaseGroup,
    string? ReleaseTitle,
    string? Indexer,
    long Size,
    int CustomFormatScore,
    List<string>? CustomFormats,
    List<RadarrLanguage>? Languages);

/// <param name="ReleaseType">Sonarr's <c>ReleaseType</c>: <c>singleEpisode</c>, <c>multiEpisode</c>, <c>seasonPack</c>, <c>unknown</c>.</param>
public sealed record SonarrWebhookGrabbedRelease(string? ReleaseTitle, string? Indexer, long? Size, string ReleaseType);

/// <summary><c>Grab</c>, and <c>Test</c> (Sonarr's test is a grab payload about a fake series).</summary>
public sealed record SonarrWebhookGrabPayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public List<SonarrWebhookEpisode>? Episodes { get; init; }

    public SonarrWebhookRelease? Release { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }
}

/// <summary><c>Download</c>: one file imported, an upgrade when <see cref="IsUpgrade"/> and <see cref="DeletedFiles"/> say so.</summary>
public sealed record SonarrWebhookImportPayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public List<SonarrWebhookEpisode>? Episodes { get; init; }

    public SonarrWebhookEpisodeFile? EpisodeFile { get; init; }

    public bool IsUpgrade { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public List<SonarrWebhookEpisodeFile>? DeletedFiles { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }

    public SonarrWebhookGrabbedRelease? Release { get; init; }
}

public sealed record SonarrWebhookRenamePayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public List<SonarrWebhookEpisodeFile>? RenamedEpisodeFiles { get; init; }
}

public sealed record SonarrWebhookSeriesAddPayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }
}

public sealed record SonarrWebhookSeriesDeletePayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public bool DeletedFiles { get; init; }
}

public sealed record SonarrWebhookEpisodeDeletePayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public List<SonarrWebhookEpisode>? Episodes { get; init; }

    public SonarrWebhookEpisodeFile? EpisodeFile { get; init; }

    /// <summary>Sonarr's <c>DeleteMediaFileReason</c>, camelCase.</summary>
    public required string DeleteReason { get; init; }
}

public sealed record SonarrWebhookManualInteractionPayload : SonarrWebhookPayload
{
    public SonarrWebhookSeries? Series { get; init; }

    public List<SonarrWebhookEpisode>? Episodes { get; init; }

    public RadarrWebhookDownloadClientItem? DownloadInfo { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public string? DownloadStatus { get; init; }

    public List<RadarrWebhookDownloadStatusMessage>? DownloadStatusMessages { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }

    public SonarrWebhookGrabbedRelease? Release { get; init; }
}

/// <summary><c>Health</c> and <c>HealthRestored</c>.</summary>
public sealed record SonarrWebhookHealthPayload : SonarrWebhookPayload
{
    /// <summary>Sonarr's <c>HealthCheckResult</c>: <c>notice</c>, <c>warning</c>, <c>error</c>.</summary>
    public string? Level { get; init; }

    public string? Message { get; init; }

    public string? Type { get; init; }

    public string? WikiUrl { get; init; }
}
