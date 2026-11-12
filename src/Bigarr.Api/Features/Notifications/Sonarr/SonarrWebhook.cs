using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Notifications.Radarr;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Notifications.Sonarr;

/// <summary>
/// Bigarr's series events as Sonarr v4's webhook sends them (<c>WebhookBase</c>), so a tool written for Sonarr's
/// webhook reads them unchanged. Built from the event's snapshot only, like the other formats. The <c>series</c> is the
/// compat line: its id is the series version's, as the Sonarr v3 surface will show it; episode ids are Bigarr's
/// shared episode ids.
/// </summary>
public static partial class SonarrWebhook
{
    public const string InstanceName = "Bigarr";

    /// <summary>The body of one delivery, or null when Sonarr's format has nothing for it.</summary>
    public static string? Body(string eventType, string payloadJson, int? versionId, string? applicationUrl)
    {
        var domainEvent = EventJson.Deserialize(eventType, payloadJson);
        var payload = domainEvent is null ? null : Payload(domainEvent, versionId, applicationUrl ?? string.Empty);
        return payload is null ? null : Serialize(payload);
    }

    public static string Serialize(SonarrWebhookPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, payload.GetType(), RadarrCompat.Json);
    }

    /// <param name="versionId">The delivery's Sonarr series among the versions of a deleted show; the event's version otherwise.</param>
    public static SonarrWebhookPayload? Payload(DomainEvent domainEvent, int? versionId, string applicationUrl)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            EpisodeGrabbed e => new SonarrWebhookGrabPayload
            {
                EventType = "Grab",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(e.Series, e.Version),
                Episodes = Episodes(e.Episodes, e.Version.Id),
                Release = new SonarrWebhookRelease(
                    e.Release.Quality ?? "Unknown",
                    e.Release.QualityVersion ?? 1,
                    e.Release.ReleaseGroup,
                    e.Release.Title,
                    e.Release.Indexer,
                    e.Release.Size,
                    e.Release.CustomFormatScore ?? 0,
                    e.Release.CustomFormats ?? [],
                    RadarrMapper.Languages(e.Release.Languages ?? [])),
                DownloadClient = e.DownloadClient,
                DownloadClientType = e.DownloadClientType,
                DownloadId = e.DownloadId,
                CustomFormatInfo = Formats(e.Release.CustomFormatIds, e.Release.CustomFormats, e.Release.CustomFormatScore),
            },
            EpisodeFileImported e => new SonarrWebhookImportPayload
            {
                EventType = "Download",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(e.Series, e.Version),
                Episodes = Episodes(e.Episodes, e.Version.Id),
                EpisodeFile = EpisodeFile(e.File),
                IsUpgrade = e.IsUpgrade,
                DownloadClient = e.DownloadClient,
                DownloadClientType = e.DownloadClientType,
                DownloadId = e.DownloadId,
                DeletedFiles = e.PreviousFiles is { Count: > 0 } previous ? previous.Select(f => EpisodeFile(f) with { SourcePath = null }).ToList() : null,
                CustomFormatInfo = Formats(e.File.CustomFormatIds, e.File.CustomFormats, e.File.CustomFormatScore),
                Release = new SonarrWebhookGrabbedRelease(e.Release?.Title, e.Release?.Indexer, e.Release?.Size, ReleaseType(e.Episodes.Count, e.Release?.Title)),
            },
            EpisodeFileRenamed e => new SonarrWebhookRenamePayload
            {
                EventType = "Rename",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(e.Series, e.Version),
                RenamedEpisodeFiles = e.Files.Select(f => EpisodeFile(f.File) with
                {
                    PreviousRelativePath = f.PreviousRelativePath ?? (e.Version.Folder is null ? Path.GetFileName(f.PreviousPath) : Path.GetRelativePath(e.Version.Folder, f.PreviousPath)),
                    PreviousPath = f.PreviousPath,
                }).ToList(),
            },
            SeriesAdded e => new SonarrWebhookSeriesAddPayload
            {
                EventType = "SeriesAdd",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(e.Series, e.Version),
            },
            SeriesDeleted e => (e.Versions ?? []).FirstOrDefault(v => versionId is null ? v.IsDefault : v.Id == versionId) is { } version
                ? new SonarrWebhookSeriesDeletePayload
                {
                    EventType = "SeriesDelete",
                    InstanceName = InstanceName,
                    ApplicationUrl = applicationUrl,
                    Series = Series(e.Series, version),
                    DeletedFiles = e.DeletedFiles,
                }
                : null,
            EpisodeFileDeleted e => new SonarrWebhookEpisodeDeletePayload
            {
                EventType = "EpisodeFileDelete",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(e.Series, e.Version),
                Episodes = Episodes(e.Episodes, e.Version.Id),

                // Sonarr does not say where a deleted file went; the Bigarr format does.
                EpisodeFile = EpisodeFile(e.File) with { RecycleBinPath = null },
                DeleteReason = e.Reason,
            },
            HealthIssueRaised e => new SonarrWebhookHealthPayload
            {
                EventType = "Health",
                InstanceName = InstanceName,
                Level = RadarrMapper.Camel(e.IssueType.ToString()),
                Message = e.Message,
                Type = e.Source,
            },
            HealthIssueResolved e => new SonarrWebhookHealthPayload
            {
                EventType = "HealthRestored",
                InstanceName = InstanceName,
                Level = e.IssueType is { } level ? RadarrMapper.Camel(level.ToString()) : null,
                Message = e.Message,
                Type = e.Source,
            },
            EpisodeImportFailed { Series: { } series, Version: { } version } e => new SonarrWebhookManualInteractionPayload
            {
                EventType = "ManualInteractionRequired",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Series = Series(series, version),
                Episodes = Episodes(e.Episodes, version.Id),
                DownloadInfo = new RadarrWebhookDownloadClientItem(e.Release.Quality ?? "Unknown", e.Release.QualityVersion ?? 1, e.Release.Title, null, e.Release.Size),
                DownloadClient = e.DownloadClient,
                DownloadClientType = e.DownloadClientType,
                DownloadId = e.DownloadId,
                DownloadStatus = "Warning",
                DownloadStatusMessages = [new RadarrWebhookDownloadStatusMessage(e.Release.Title, [e.Reason])],
                CustomFormatInfo = Formats(e.Release.CustomFormatIds, e.Release.CustomFormats, e.Release.CustomFormatScore),
                Release = new SonarrWebhookGrabbedRelease(e.Release.Title, e.Release.Indexer, e.Release.Size, ReleaseType(e.Episodes.Count, e.Release.Title)),
            },
            TestEvent => Test(applicationUrl),
            _ => null,
        };
    }

    /// <summary>Sonarr's test payload, word for word: a grab of a fake series.</summary>
    public static SonarrWebhookGrabPayload Test(string applicationUrl) => new()
    {
        EventType = "Test",
        InstanceName = InstanceName,
        ApplicationUrl = applicationUrl,
        Series = new SonarrWebhookSeries(1, "Test Title", null, "C:\\testpath", 1234, 0, 0, null, "standard", 0, null, null, ["test-tag"], null),
        Episodes = [new SonarrWebhookEpisode(123, 1, 1, "Test title", null, null, null, 0, 0)],
    };

    /// <summary>Sonarr's <c>WebhookSeries</c> for one version of a show: the version's folder is its path.</summary>
    public static SonarrWebhookSeries Series(SeriesRef series, SeriesVersionRef version)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(version);

        return new SonarrWebhookSeries(
            version.Id,
            series.Title,
            Slug(series.Title),
            version.Folder,
            series.TvdbId ?? 0,
            0,
            series.TmdbId ?? 0,
            series.ImdbId,
            RadarrMapper.Camel(series.Type.ToString()),
            series.Year ?? 0,
            series.Genres ?? [],
            Images(series, version.Id),
            (version.Tags ?? []).Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Order(StringComparer.Ordinal).ToList(),
            RadarrMapper.OriginalLanguage(series.OriginalLanguage));
    }

    public static List<SonarrWebhookEpisode> Episodes(IEnumerable<EpisodeRef> episodes, int seriesId)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        return episodes.Select(e => new SonarrWebhookEpisode(
            e.Id,
            e.EpisodeNumber,
            e.SeasonNumber,
            e.Title,
            e.Overview,
            e.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            e.AirDateUtc,
            seriesId,
            e.TvdbId ?? 0)).ToList();
    }

    public static SonarrWebhookEpisodeFile EpisodeFile(FileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var radarr = RadarrWebhook.MovieFile(file);
        return new SonarrWebhookEpisodeFile
        {
            Id = file.Id,
            RelativePath = radarr.RelativePath,
            Path = file.Path,
            Quality = file.Quality,
            QualityVersion = file.QualityVersion ?? 1,
            ReleaseGroup = file.ReleaseGroup,
            SceneName = file.SceneName,
            Size = file.Size,
            DateAdded = file.DateAdded,
            Languages = radarr.Languages,
            MediaInfo = radarr.MediaInfo,
            SourcePath = file.SourcePath,
            RecycleBinPath = file.RecycleBinPath,
        };
    }

    /// <summary>Sonarr's <c>TitleSlug</c>: lower case, words joined by dashes.</summary>
    public static string Slug(string title) => SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-');

    /// <summary>Sonarr's <c>ReleaseType</c> of the release a file came from: a pack, several episodes, or one.</summary>
    private static string ReleaseType(int episodes, string? releaseTitle)
    {
        if (releaseTitle is not null)
        {
            try
            {
                if (Bigarr.Parsing.Episodes.Parser.ParseTitle(releaseTitle) is { FullSeason: true })
                {
                    return "seasonPack";
                }
            }
            catch (Exception)
            {
                // not parsable: judged on the episode count
            }
        }

        return episodes switch
        {
            1 => "singleEpisode",
            > 1 => "multiEpisode",
            _ => "unknown",
        };
    }

    private static RadarrWebhookCustomFormatInfo Formats(List<int>? ids, List<string>? names, int? score) =>
        new((names ?? []).Select((name, i) => new RadarrCustomFormat(ids is not null && i < ids.Count ? ids[i] : 0, name)).ToList(), score ?? 0);

    private static List<RadarrImage> Images(SeriesRef series, int versionId)
    {
        var images = new List<RadarrImage>();
        foreach (var type in (ImageType[])[ImageType.Poster, ImageType.Fanart])
        {
            if (series.Images?.FirstOrDefault(i => i.CoverType == type) is { } image)
            {
                var name = RadarrMapper.Camel(type.ToString());
                images.Add(new RadarrImage(name, $"/MediaCover/{versionId}/{name}.jpg", image.RemoteUrl));
            }
        }

        return images;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
