using System.Globalization;
using System.Text.Json;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Infrastructure.Events;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Events;
using Bigarr.Domain.Movies;

namespace Bigarr.Api.Features.Notifications.Radarr;

/// <summary>
/// Bigarr events as Radarr's webhook sends them (<c>WebhookBase</c>), so a tool written for Radarr's webhook reads them
/// unchanged. Built from the event's snapshot only, the same data the Bigarr format carries, so both describe the
/// moment of the event. The <c>movie</c> is the compat line: its id is the version's, as on <c>/api/v3/movie</c>.
/// </summary>
public static class RadarrWebhook
{
    public const string InstanceName = "Bigarr";

    /// <summary>Radarr's <c>IndexerFlags</c>, bit for bit the values Bigarr stores.</summary>
    private static readonly (int Bit, string Name)[] IndexerFlagNames =
    [
        (1, "G_Freeleech"),
        (2, "G_Halfleech"),
        (4, "G_DoubleUpload"),
        (8, "PTP_Golden"),
        (16, "PTP_Approved"),
        (32, "G_Internal"),
        (64, "AHD_Internal"),
        (128, "G_Scene"),
        (256, "G_Freeleech75"),
        (512, "G_Freeleech25"),
        (1024, "AHD_UserRelease"),
        (2048, "Nuked"),
    ];

    /// <summary>The body of one delivery, or null when Radarr's format has nothing for it.</summary>
    public static string? Body(string eventType, string payloadJson, int? versionId, string? applicationUrl)
    {
        var domainEvent = EventJson.Deserialize(eventType, payloadJson);
        var payload = domainEvent is null ? null : Payload(domainEvent, versionId, applicationUrl ?? string.Empty);
        return payload is null ? null : Serialize(payload);
    }

    public static string Serialize(RadarrWebhookPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, payload.GetType(), RadarrCompat.Json);
    }

    /// <summary>
    /// The payload of one delivery, or null when Radarr's format has nothing for it: an event Radarr has no word for,
    /// an import failure about no film.
    /// </summary>
    /// <param name="versionId">The delivery's Radarr movie, among the versions of a deleted film; its default version otherwise.</param>
    public static RadarrWebhookPayload? Payload(DomainEvent domainEvent, int? versionId, string applicationUrl)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return domainEvent switch
        {
            ReleaseGrabbed e => Grab(e, applicationUrl),
            FileImported e => Download(e.Movie, e.Version, e.File, null, e.Release, e.DownloadClient, e.DownloadClientType, e.DownloadId, applicationUrl),
            FileUpgraded e => Download(e.Movie, e.Version, e.File, e.PreviousFile, e.Release, e.DownloadClient, e.DownloadClientType, e.DownloadId, applicationUrl),
            FileRenamed e => new RadarrWebhookRenamePayload
            {
                EventType = "Rename",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Movie = Movie(e.Movie, e.Version),
                RenamedMovieFiles =
                [
                    MovieFile(e.File) with
                    {
                        PreviousRelativePath = e.Version.Folder is null ? Path.GetFileName(e.PreviousPath) : Path.GetRelativePath(e.Version.Folder, e.PreviousPath),
                        PreviousPath = e.PreviousPath,
                    },
                ],
            },
            MovieAdded e => new RadarrWebhookAddedPayload
            {
                EventType = "MovieAdded",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Movie = Movie(e.Movie, e.Version),
                AddMethod = "manual",
            },
            MovieDeleted e => (e.Versions ?? []).FirstOrDefault(v => versionId is null ? v.IsDefault : v.Id == versionId) is { } version
                ? new RadarrWebhookMovieDeletePayload
                {
                    EventType = "MovieDelete",
                    InstanceName = InstanceName,
                    ApplicationUrl = applicationUrl,
                    Movie = Movie(e.Movie, version),
                    DeletedFiles = e.DeletedFiles,
                    MovieFolderSize = e.DeletedFiles ? version.SizeOnDisk ?? 0 : 0,
                }
                : null,
            FileDeleted e => new RadarrWebhookMovieFileDeletePayload
            {
                EventType = "MovieFileDelete",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Movie = Movie(e.Movie, e.Version),

                // Radarr does not say where a deleted file went; the Bigarr format does.
                MovieFile = MovieFile(e.File) with { RecycleBinPath = null },
                DeleteReason = "manual",
            },
            HealthIssueRaised e => new RadarrWebhookHealthPayload
            {
                EventType = "Health",
                InstanceName = InstanceName,
                ApplicationUrl = applicationUrl,
                Level = RadarrMapper.Camel(e.IssueType.ToString()),
                Message = e.Message,
                Type = e.Source,
            },

            // Radarr's HealthRestored leaves the application URL out.
            HealthIssueResolved e => new RadarrWebhookHealthPayload
            {
                EventType = "HealthRestored",
                InstanceName = InstanceName,
                Level = e.IssueType is { } level ? RadarrMapper.Camel(level.ToString()) : null,
                Message = e.Message,
                Type = e.Source,
            },
            ImportFailed { Movie: { } movie, Version: { } version } e => ManualInteraction(e, movie, version, applicationUrl),
            TestEvent => Test(applicationUrl),
            _ => null,
        };
    }

    /// <summary>Radarr's test payload, word for word: a grab of a fake film.</summary>
    public static RadarrWebhookGrabPayload Test(string applicationUrl) => new()
    {
        EventType = "Test",
        InstanceName = InstanceName,
        ApplicationUrl = applicationUrl,
        Movie = new RadarrWebhookMovie(1, "Test Title", 1970, null, "1970-01-01", "C:\\testpath", 0, null, null, null, null, ["test-tag"], null),
        RemoteMovie = new RadarrWebhookRemoteMovie(1234, "5678", "Test title", 1970),
        Release = new RadarrWebhookRelease("Test Quality", 1, "Test Group", "Test Title", "Test Indexer", 9999999, 0, null, null, null),
    };

    /// <summary>Radarr's <c>WebhookMovie</c> for one version of a film. Its <c>filePath</c> stays empty, as in Radarr.</summary>
    public static RadarrWebhookMovie Movie(MovieRef movie, VersionRef version)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentNullException.ThrowIfNull(version);

        return new RadarrWebhookMovie(
            version.Id,
            movie.Title,
            movie.Year ?? 0,
            null,
            ReleaseDate(movie),
            version.Folder,
            movie.TmdbId,
            movie.ImdbId,
            movie.Overview,
            movie.Genres ?? [],
            Images(movie, version.Id),
            (version.Tags ?? []).Select(t => t.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Order(StringComparer.Ordinal).ToList(),
            RadarrMapper.OriginalLanguage(movie.OriginalLanguage));
    }

    /// <summary>Radarr's <c>PhysicalReleaseDate()</c>: the physical release, else 90 days after the cinema release, else the end of time.</summary>
    public static string ReleaseDate(MovieRef movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        var date = movie.PhysicalRelease ?? movie.InCinemas?.AddDays(90) ?? DateOnly.MaxValue;
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Radarr's <c>IndexerFlags.ToString()</c>: <c>0</c> when none, else the names joined by a comma.</summary>
    public static string FlagsText(IndexerFlags flags) =>
        flags == IndexerFlags.None ? "0" : string.Join(", ", FlagNames(flags));

    public static List<string> FlagNames(IndexerFlags? flags) =>
        IndexerFlagNames.Where(f => ((int)(flags ?? IndexerFlags.None) & f.Bit) == f.Bit).Select(f => f.Name).ToList();

    public static RadarrWebhookMovieFile MovieFile(FileRef file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new RadarrWebhookMovieFile
        {
            Id = file.Id,
            RelativePath = file.RelativePath ?? Path.GetFileName(file.Path),
            Path = file.Path,
            Quality = file.Quality,
            QualityVersion = file.QualityVersion,
            ReleaseGroup = file.ReleaseGroup,
            SceneName = file.SceneName,
            IndexerFlags = file.IndexerFlags is { } flags ? FlagsText(flags) : null,
            Size = file.Size,
            DateAdded = file.DateAdded,
            Languages = file.Languages is null ? null : RadarrMapper.Languages(file.Languages),
            MediaInfo = file.MediaInfo is { } info ? MediaInfo(info) : null,
            SourcePath = file.SourcePath,
            RecycleBinPath = file.RecycleBinPath,
        };
    }

    private static RadarrWebhookGrabPayload Grab(ReleaseGrabbed e, string applicationUrl) => new()
    {
        EventType = "Grab",
        InstanceName = InstanceName,
        ApplicationUrl = applicationUrl,
        Movie = Movie(e.Movie, e.Version),
        RemoteMovie = RemoteMovie(e.Movie),
        Release = new RadarrWebhookRelease(
            e.Release.Quality ?? "Unknown",
            e.Release.QualityVersion ?? 1,
            e.Release.ReleaseGroup,
            e.Release.Title,
            e.Release.Indexer,
            e.Release.Size,
            e.Release.CustomFormatScore ?? 0,
            e.Release.CustomFormats ?? [],
            RadarrMapper.Languages(e.Release.Languages ?? []),
            FlagNames(e.Release.IndexerFlags)),
        DownloadClient = e.DownloadClient,
        DownloadClientType = e.DownloadClientType,
        DownloadId = e.DownloadId,
        CustomFormatInfo = Formats(e.Release.CustomFormatIds, e.Release.CustomFormats, e.Release.CustomFormatScore),
    };

    private static RadarrWebhookImportPayload Download(
        MovieRef movie,
        VersionRef version,
        FileRef file,
        FileRef? previous,
        ReleaseRef? release,
        string? downloadClient,
        string? downloadClientType,
        string? downloadId,
        string applicationUrl) => new()
        {
            EventType = "Download",
            InstanceName = InstanceName,
            ApplicationUrl = applicationUrl,
            Movie = Movie(movie, version),
            RemoteMovie = RemoteMovie(movie),
            MovieFile = MovieFile(file),
            IsUpgrade = previous is not null,
            DownloadClient = downloadClient,
            DownloadClientType = downloadClientType,
            DownloadId = downloadId,
            DeletedFiles = previous is null ? null : [MovieFile(previous)],
            CustomFormatInfo = Formats(file.CustomFormatIds, file.CustomFormats, file.CustomFormatScore),

            // Radarr's WebhookGrabbedRelease: the release's flags, or the file's when it was imported without one.
            Release = release is null
                ? new RadarrWebhookGrabbedRelease(null, null, 0, FlagNames(file.IndexerFlags))
                : new RadarrWebhookGrabbedRelease(release.Title, release.Indexer, release.Size, FlagNames(release.IndexerFlags ?? file.IndexerFlags)),
        };

    private static RadarrWebhookManualInteractionPayload ManualInteraction(ImportFailed e, MovieRef movie, VersionRef version, string applicationUrl) => new()
    {
        EventType = "ManualInteractionRequired",
        InstanceName = InstanceName,
        ApplicationUrl = applicationUrl,
        Movie = Movie(movie, version),
        DownloadInfo = new RadarrWebhookDownloadClientItem(e.Release.Quality ?? "Unknown", e.Release.QualityVersion ?? 1, e.Release.Title, null, e.Release.Size),
        DownloadClient = e.DownloadClient,
        DownloadClientType = e.DownloadClientType,
        DownloadId = e.DownloadId,
        DownloadStatus = "Warning",
        DownloadStatusMessages = [new RadarrWebhookDownloadStatusMessage(e.Release.Title, [e.Reason])],
        CustomFormatInfo = Formats(e.Release.CustomFormatIds, e.Release.CustomFormats, e.Release.CustomFormatScore),
        Release = new RadarrWebhookGrabbedRelease(e.Release.Title, e.Release.Indexer, e.Release.Size, FlagNames(e.Release.IndexerFlags)),
    };

    private static RadarrWebhookRemoteMovie RemoteMovie(MovieRef movie) => new(movie.TmdbId, movie.ImdbId, movie.Title, movie.Year ?? 0);

    private static RadarrWebhookCustomFormatInfo Formats(List<int>? ids, List<string>? names, int? score) =>
        new((names ?? []).Select((name, i) => new RadarrCustomFormat(ids is not null && i < ids.Count ? ids[i] : 0, name)).ToList(), score ?? 0);

    /// <summary>Poster first, fanart second, served by the compat <c>/MediaCover</c> under the version's id, as <see cref="RadarrMapper.Images"/> does.</summary>
    private static List<RadarrImage> Images(MovieRef movie, int versionId)
    {
        var images = new List<RadarrImage>();
        foreach (var type in (ImageType[])[ImageType.Poster, ImageType.Fanart])
        {
            if (movie.Images?.FirstOrDefault(i => i.CoverType == type) is { } image)
            {
                var name = RadarrMapper.Camel(type.ToString());
                images.Add(new RadarrImage(name, $"/MediaCover/{versionId}/{name}.jpg", image.RemoteUrl));
            }
        }

        return images;
    }

    private static RadarrWebhookMediaInfo MediaInfo(MediaInfoRef info) => new(
        (decimal)(info.AudioChannels ?? 0),
        info.AudioCodec,
        info.AudioLanguages.Distinct(StringComparer.Ordinal).ToList(),
        info.Height ?? 0,
        info.Width ?? 0,
        info.Subtitles.Distinct(StringComparer.Ordinal).ToList(),
        info.VideoCodec,
        info.VideoDynamicRange,
        info.VideoDynamicRangeType);
}
