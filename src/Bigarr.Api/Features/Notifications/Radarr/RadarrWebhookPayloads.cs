using System.Text.Json.Serialization;
using Bigarr.Api.Features.Compat.Radarr;

namespace Bigarr.Api.Features.Notifications.Radarr;

/// <summary>
/// Radarr's webhook bodies (<c>NzbDrone.Core/Notifications/Webhook</c>), one record per payload class, same member
/// names. Radarr serializes them with Newtonsoft (camelCase, nulls left out, enums as camelCase strings except
/// <c>eventType</c>, which keeps Radarr's PascalCase): <see cref="RadarrCompat.Json"/> writes the same thing.
/// </summary>
public abstract record RadarrWebhookPayload
{
    [JsonPropertyOrder(-3)]
    public required string EventType { get; init; }

    [JsonPropertyOrder(-2)]
    public string? InstanceName { get; init; }

    [JsonPropertyOrder(-1)]
    public string? ApplicationUrl { get; init; }
}

/// <param name="Id">The compat movie id, i.e. the version id: the same <c>id</c> as <c>GET /api/v3/movie</c> (§7.1).</param>
/// <param name="FilePath">Declared by Radarr but never filled by its webhook; kept for the shape.</param>
/// <param name="ReleaseDate">Radarr's <c>PhysicalReleaseDate()</c>, <c>yyyy-MM-dd</c>.</param>
public sealed record RadarrWebhookMovie(
    int Id,
    string Title,
    int Year,
    string? FilePath,
    string? ReleaseDate,
    string? FolderPath,
    int TmdbId,
    string? ImdbId,
    string? Overview,
    List<string>? Genres,
    List<RadarrImage>? Images,
    List<string>? Tags,
    RadarrLanguage? OriginalLanguage);

public sealed record RadarrWebhookRemoteMovie(int TmdbId, string? ImdbId, string Title, int Year);

/// <summary>What Radarr sends on a grab: the parsed release as it was decided.</summary>
public sealed record RadarrWebhookRelease(
    string? Quality,
    int QualityVersion,
    string? ReleaseGroup,
    string? ReleaseTitle,
    string? Indexer,
    long Size,
    int CustomFormatScore,
    List<string>? CustomFormats,
    List<RadarrLanguage>? Languages,
    List<string>? IndexerFlags);

/// <summary>The release a file came from (Radarr's <c>WebhookGrabbedRelease</c>); only the flags when it was imported by hand.</summary>
public sealed record RadarrWebhookGrabbedRelease(string? ReleaseTitle, string? Indexer, long Size, List<string>? IndexerFlags);

public sealed record RadarrWebhookCustomFormatInfo(List<RadarrCustomFormat> CustomFormats, int CustomFormatScore);

public sealed record RadarrWebhookMediaInfo(
    decimal AudioChannels,
    string? AudioCodec,
    List<string>? AudioLanguages,
    int Height,
    int Width,
    List<string>? Subtitles,
    string? VideoCodec,
    string? VideoDynamicRange,
    string? VideoDynamicRangeType);

/// <summary>
/// Radarr's <c>WebhookMovieFile</c>, and its <c>WebhookRenamedMovieFile</c> subclass through the two <c>Previous*</c>
/// members. Members Radarr always fills are nullable here when the event only kept a snapshot of a file that is gone.
/// </summary>
public sealed record RadarrWebhookMovieFile
{
    public int Id { get; init; }

    public string? RelativePath { get; init; }

    public string? Path { get; init; }

    public string? Quality { get; init; }

    public int? QualityVersion { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? SceneName { get; init; }

    /// <summary>The flags enum's <c>ToString()</c>, as Radarr writes it: <c>0</c>, <c>G_Freeleech</c>, <c>G_Freeleech, G_Internal</c>.</summary>
    public string? IndexerFlags { get; init; }

    public long Size { get; init; }

    public DateTime? DateAdded { get; init; }

    public List<RadarrLanguage>? Languages { get; init; }

    public RadarrWebhookMediaInfo? MediaInfo { get; init; }

    public string? SourcePath { get; init; }

    public string? RecycleBinPath { get; init; }

    public string? PreviousRelativePath { get; init; }

    public string? PreviousPath { get; init; }
}

public sealed record RadarrWebhookDownloadClientItem(string? Quality, int QualityVersion, string? Title, string? Indexer, long Size);

public sealed record RadarrWebhookDownloadStatusMessage(string Title, List<string> Messages);

/// <summary><c>Grab</c>, and <c>Test</c> (Radarr's test is a grab payload about a fake film).</summary>
public sealed record RadarrWebhookGrabPayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public RadarrWebhookRemoteMovie? RemoteMovie { get; init; }

    public RadarrWebhookRelease? Release { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }
}

/// <summary><c>Download</c>: an import, or an upgrade when <see cref="IsUpgrade"/> and <see cref="DeletedFiles"/> say so.</summary>
public sealed record RadarrWebhookImportPayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public RadarrWebhookRemoteMovie? RemoteMovie { get; init; }

    public RadarrWebhookMovieFile? MovieFile { get; init; }

    public bool IsUpgrade { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public List<RadarrWebhookMovieFile>? DeletedFiles { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }

    public RadarrWebhookGrabbedRelease? Release { get; init; }
}

public sealed record RadarrWebhookRenamePayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public List<RadarrWebhookMovieFile>? RenamedMovieFiles { get; init; }
}

public sealed record RadarrWebhookAddedPayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    /// <summary>Radarr's <c>AddMovieMethod</c>: <c>manual</c>, <c>list</c> or <c>collection</c>.</summary>
    public required string AddMethod { get; init; }
}

public sealed record RadarrWebhookMovieDeletePayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public bool DeletedFiles { get; init; }

    public long MovieFolderSize { get; init; }
}

public sealed record RadarrWebhookMovieFileDeletePayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public RadarrWebhookMovieFile? MovieFile { get; init; }

    /// <summary>Radarr's <c>DeleteMediaFileReason</c>, camelCase.</summary>
    public required string DeleteReason { get; init; }
}

/// <summary><c>Health</c> and <c>HealthRestored</c>.</summary>
public sealed record RadarrWebhookHealthPayload : RadarrWebhookPayload
{
    /// <summary>Radarr's <c>HealthCheckResult</c>: <c>notice</c>, <c>warning</c>, <c>error</c>.</summary>
    public string? Level { get; init; }

    public string? Message { get; init; }

    /// <summary>The check that raised it; Radarr names its check class, Bigarr its source.</summary>
    public string? Type { get; init; }

    public string? WikiUrl { get; init; }
}

public sealed record RadarrWebhookManualInteractionPayload : RadarrWebhookPayload
{
    public RadarrWebhookMovie? Movie { get; init; }

    public RadarrWebhookDownloadClientItem? DownloadInfo { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    /// <summary>Radarr's <c>TrackedDownloadStatus.ToString()</c>, PascalCase.</summary>
    public string? DownloadStatus { get; init; }

    public List<RadarrWebhookDownloadStatusMessage>? DownloadStatusMessages { get; init; }

    public RadarrWebhookCustomFormatInfo? CustomFormatInfo { get; init; }

    public RadarrWebhookGrabbedRelease? Release { get; init; }
}
