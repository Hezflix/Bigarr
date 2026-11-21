using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Events;
using Bigarr.Domain.Issues;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Requests;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Notifications.Discord;

/// <summary>A film from the library, as its events would describe it: the version with a file, that file and the release it came from.</summary>
/// <param name="OtherVersion">Another version of the film, the previous default of a default version change; null when it has one.</param>
public sealed record PreviewFilm(MovieRef Movie, VersionRef Version, FileRef File, ReleaseRef Release, VersionRef? OtherVersion = null);

/// <summary>A show from the library, as its events would describe it: a version, one episode file, its episodes and its release.</summary>
public sealed record PreviewSeries(SeriesRef Series, SeriesVersionRef Version, List<EpisodeRef> Episodes, FileRef File, ReleaseRef Release);

/// <summary>
/// What the Discord form shows before anything is posted: one sample event per message the Discord format sends, built
/// around a film and a show of the library, run through <see cref="DiscordWebhook.Payload"/> as a delivery would be.
/// Nothing here reaches Discord.
/// </summary>
public static class DiscordPreview
{
    private const string Client = "qBittorrent";
    private const string ClientType = "QBittorrent";
    private const string DownloadId = "0123456789ABCDEF";
    private const string Requester = "requester";
    private const string Approver = "admin";

    /// <summary>The Discord message for every event type the Discord format posts, in the catalog's order.</summary>
    public static List<(string EventType, DiscordPayload Payload)> All(PreviewFilm? film, PreviewSeries? series, DiscordSettings? settings, DateTime now, string? applicationUrl)
    {
        film ??= SampleFilm(now);
        series ??= SampleSeries(now);
        var context = new DiscordContext(applicationUrl);
        return DomainEventCatalog.Entries
            .Where(e => WebhookRouting.SentToDiscord(e.Type))
            .Select(e => (e.Type, Event: Event(e.Type, film, series, now)))
            .Select(e => (e.Type, Payload: e.Event is null ? null : DiscordWebhook.Payload(e.Event, settings, context)))
            .Where(e => e.Payload is not null)
            .Select(e => (e.Type, e.Payload!))
            .ToList();
    }

    /// <summary>A sample of one event type around the film and the show; null for a type Discord is not sent.</summary>
    public static DomainEvent? Event(string eventType, PreviewFilm film, PreviewSeries series, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(film);
        ArgumentNullException.ThrowIfNull(series);
        var (m, v, f, r) = (film.Movie, film.Version, film.File, film.Release);
        var previous = f with { Id = f.Id + 1, Quality = PreviousQuality(f.Quality), Path = Path.Combine(Path.GetDirectoryName(f.Path) ?? string.Empty, "previous.mkv") };
        var request = RequestOf(m);
        var issue = IssueOf(m, v);
        var name = DomainEventCatalog.Entries.FirstOrDefault(e => e.Type == eventType).Clr?.Name;
        DomainEvent? sample = name switch
        {
            nameof(ReleaseGrabbed) => new ReleaseGrabbed(m, v, r, 1, Client, DownloadId, "rss", ClientType),
            nameof(FileImported) => new FileImported(m, v, f, r, 1, false, "hardlink", Client, ClientType, DownloadId),
            nameof(FileUpgraded) => new FileUpgraded(m, v, f, previous, r, 1, false, "hardlink", Client, ClientType, DownloadId),
            nameof(FileRenamed) => new FileRenamed(m, v, f, previous.Path),
            nameof(MovieAdded) => new MovieAdded(m, v),
            nameof(MovieDeleted) => new MovieDeleted(m, false, [v]),
            nameof(FileDeleted) => new FileDeleted(m, v, f, "recycled"),
            nameof(VersionAdded) => new VersionAdded(m, v),
            nameof(VersionDeleted) => new VersionDeleted(m, v, false),
            nameof(DefaultVersionChanged) => new DefaultVersionChanged(m, v, film.OtherVersion),
            nameof(HealthIssueRaised) => new HealthIssueRaised("IndexerStatusCheck", HealthIssueType.Warning, "Indexers unavailable due to failures: Example indexer"),
            nameof(HealthIssueResolved) => new HealthIssueResolved("IndexerStatusCheck", "Indexers unavailable due to failures: Example indexer", HealthIssueType.Warning),
            nameof(ImportFailed) => new ImportFailed(m, v, r, 1, "No files found are eligible for import", Client, ClientType, DownloadId),
            nameof(DownloadFailed) => new DownloadFailed(m, v, r, 1, "The download client reported the download as failed", true, true),
            nameof(TestEvent) => new TestEvent("Bigarr can reach this channel."),
            nameof(EpisodeGrabbed) => new EpisodeGrabbed(series.Series, series.Version, series.Episodes, series.Release, 2, Client, DownloadId, "rss", ClientType),
            nameof(EpisodeFileImported) => new EpisodeFileImported(series.Series, series.Version, series.Episodes, series.File, null, series.Release, 2, false, "hardlink", Client, ClientType, DownloadId),
            nameof(EpisodeFileRenamed) => new EpisodeFileRenamed(series.Series, series.Version, [new RenamedFileRef(series.File, Path.Combine(series.Version.Folder ?? string.Empty, "previous.mkv"), "previous.mkv")]),
            nameof(SeriesAdded) => new SeriesAdded(series.Series, series.Version),
            nameof(SeriesDeleted) => new SeriesDeleted(series.Series, false, [series.Version]),
            nameof(SeriesVersionDeleted) => new SeriesVersionDeleted(series.Series, series.Version, false),
            nameof(EpisodeFileDeleted) => new EpisodeFileDeleted(series.Series, series.Version, series.Episodes, series.File, "recycled"),
            nameof(EpisodeImportFailed) => new EpisodeImportFailed(series.Series, series.Version, series.Episodes, series.Release, 2, "No files found are eligible for import", Client, ClientType, DownloadId),
            nameof(EpisodeDownloadFailed) => new EpisodeDownloadFailed(series.Series, series.Version, series.Episodes, series.Release, 2, "The download client reported the download as failed", true, true),
            nameof(RequestCreated) => new RequestCreated(request, false),
            nameof(RequestApproved) => new RequestApproved(request, Approver, false),
            nameof(RequestDeclined) => new RequestDeclined(request, Approver, "Already in the library"),
            nameof(RequestAvailable) => new RequestAvailable(request),
            nameof(RequestFailed) => new RequestFailed(request, "No release matched the profile", false),
            nameof(IssueCreated) => new IssueCreated(issue),
            nameof(IssueCommented) => new IssueCommented(issue, 1, "Looking into it.", Approver, 1),
            nameof(IssueResolved) => new IssueResolved(issue with { Status = IssueStatus.Resolved }, Approver),
            nameof(IssueReopened) => new IssueReopened(issue, Requester),
            _ => null,
        };
        return sample is null ? null : sample with { OccurredAt = now };
    }

    /// <summary>A made-up film, for a library with no film on disk yet.</summary>
    public static PreviewFilm SampleFilm(DateTime now)
    {
        const string folder = "/movies/Sample Film (2024)";
        const string releaseTitle = "Sample.Film.2024.1080p.BluRay.x264-GROUP";
        var movie = new MovieRef(0, 0, null, "Sample Film", 2024, "What a film from your library will look like in the channel.", ["Drama"]);
        var version = new VersionRef(0, "default", VersionMode.Managed, true, true, "HD-1080p", folder, null, Path.Combine(folder, "Sample Film (2024) [Bluray-1080p].mkv"), 8_500_000_000);
        var release = new ReleaseRef(releaseTitle, "Bluray-1080p", "Example indexer", 8_500_000_000, "GROUP", 42, null, 1, ["English"]);
        var file = new FileRef(
            0, version.FilePath!, "Bluray-1080p", 8_500_000_000, RelativePath: "Sample Film (2024) [Bluray-1080p].mkv", QualityVersion: 1, ReleaseGroup: "GROUP", SceneName: releaseTitle,
            Languages: ["English"], MediaInfo: new MediaInfoRef(5.1, "DTS", ["eng"], 1920, 1080, ["eng"], "x264", null, null), DateAdded: now);
        return new PreviewFilm(movie, version, file, release);
    }

    /// <summary>A made-up show, for a library with no episode on disk yet.</summary>
    public static PreviewSeries SampleSeries(DateTime now)
    {
        const string folder = "/tv/Sample Show";
        const string releaseTitle = "Sample.Show.S01E01.1080p.WEB-DL.H.264-GROUP";
        var series = new SeriesRef(0, null, null, null, "Sample Show", 2024, SeriesType.Standard, "What a show from your library will look like in the channel.", ["Drama"]);
        var version = new SeriesVersionRef(0, "default", true, true, "HD-1080p", folder);
        var episodes = new List<EpisodeRef> { new(0, 1, 1, "Pilot", AirDate: DateOnly.FromDateTime(now)) };
        var release = new ReleaseRef(releaseTitle, "WEBDL-1080p", "Example indexer", 2_100_000_000, "GROUP", 42, null, 1, ["English"]);
        var file = new FileRef(
            0, Path.Combine(folder, "Season 01", "Sample Show - S01E01 - Pilot [WEBDL-1080p].mkv"), "WEBDL-1080p", 2_100_000_000, RelativePath: "Season 01/Sample Show - S01E01 - Pilot [WEBDL-1080p].mkv",
            QualityVersion: 1, ReleaseGroup: "GROUP", SceneName: releaseTitle, Languages: ["English"], MediaInfo: new MediaInfoRef(5.1, "EAC3", ["eng"], 1920, 1080, ["eng"], "h264", null, null), DateAdded: now);
        return new PreviewSeries(series, version, episodes, file, release);
    }

    /// <summary>The release a file came from, as far as the file remembers it: its scene name, quality, group and languages.</summary>
    public static ReleaseRef ReleaseOf(FileRef file, string? indexer) => new(
        file.SceneName ?? Path.GetFileNameWithoutExtension(file.RelativePath ?? file.Path),
        file.Quality,
        indexer,
        file.Size,
        file.ReleaseGroup,
        null,
        null,
        file.QualityVersion,
        file.Languages,
        file.IndexerFlags,
        file.CustomFormats,
        file.CustomFormatIds,
        file.CustomFormatScore);

    private static string? PreviousQuality(string? quality) => quality switch
    {
        null => null,
        _ when quality.Contains("2160p", StringComparison.Ordinal) => quality.Replace("2160p", "1080p", StringComparison.Ordinal),
        _ when quality.Contains("1080p", StringComparison.Ordinal) => quality.Replace("1080p", "720p", StringComparison.Ordinal),
        _ => quality,
    };

    private static string? Poster(MovieRef movie) => movie.Images?.FirstOrDefault(i => i.CoverType == ImageType.Poster)?.RemoteUrl;

    private static RequestRef RequestOf(MovieRef movie) =>
        new(1, RequestMediaType.Movie, movie.TmdbId == 0 ? null : movie.TmdbId, null, movie.Title, movie.Year, Poster(movie), false, [], Requester, 0);

    private static IssueRef IssueOf(MovieRef movie, VersionRef version) =>
        new(1, IssueType.Audio, IssueScope.Movie, IssueStatus.Open, RequestMediaType.Movie, movie.TmdbId == 0 ? null : movie.TmdbId, null, movie.Title, movie.Year, Poster(movie),
            null, null, version.Name, "The audio is out of sync from the start.", Requester, 0);
}
