using System.Text.Json.Serialization;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Events;

/// <summary>
/// What happened, with enough of a snapshot that a consumer never has to query Bigarr back. Each
/// record is stored as JSON in the outbox and sent as the <c>data</c> of a webhook or SSE event, so the shapes here
/// are a public contract: add fields, never rename them.
/// </summary>
public abstract record DomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>camelCase name used by the outbox, the SSE stream and webhook subscriptions.</summary>
    [JsonIgnore]
    public string Type => DomainEventCatalog.NameOf(GetType());
}

/// <param name="Images">Poster and fanart as TMDB serves them.</param>
/// <param name="OriginalLanguage">ISO 639-1 code as TMDB gives it (<c>en</c>).</param>
/// <param name="Ratings">TMDB's and IMDb's scores, when the metadata has them.</param>
public sealed record MovieRef(
    int Id,
    int TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    string? Overview = null,
    List<string>? Genres = null,
    List<ImageRef>? Images = null,
    DateOnly? InCinemas = null,
    DateOnly? PhysicalRelease = null,
    DateOnly? DigitalRelease = null,
    string? OriginalLanguage = null,
    RatingsRef? Ratings = null)
{
    public static MovieRef From(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new MovieRef(
            movie.Id,
            movie.TmdbId,
            movie.ImdbId,
            movie.Title,
            movie.Year,
            movie.Overview,
            movie.Genres.Count == 0 ? null : [.. movie.Genres],
            movie.Images.Count == 0 ? null : movie.Images.Select(i => new ImageRef(i.Type, i.RemoteUrl)).ToList(),
            movie.InCinemasDate,
            movie.PhysicalReleaseDate,
            movie.DigitalReleaseDate,
            movie.OriginalLanguage,
            movie.Ratings is { } ratings && (ratings.Tmdb is not null || ratings.Imdb is not null) ? new RatingsRef(ratings.Tmdb, ratings.Imdb) : null);
    }
}

public sealed record ImageRef(ImageType CoverType, string RemoteUrl);

public sealed record RatingsRef(RatingValue? Tmdb, RatingValue? Imdb);

public sealed record TagRef(int Id, string Label);

/// <param name="Folder">The version's folder on disk, when the root folder was loaded with it.</param>
/// <param name="Tags">
/// The version's tags, when they were loaded with it. Webhook routing uses them, and reads the current tags of a
/// version whose snapshot has none in case they were simply not loaded.
/// </param>
/// <param name="FilePath">The version's file at that moment, when the file and the root folder were loaded with it.</param>
/// <param name="SizeOnDisk">The size of that file.</param>
public sealed record VersionRef(
    int Id,
    string Name,
    VersionMode Mode,
    bool IsDefault,
    bool Monitored,
    string? QualityProfile,
    string? Folder = null,
    List<TagRef>? Tags = null,
    string? FilePath = null,
    long? SizeOnDisk = null)
{
    public static VersionRef From(MovieVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var folder = version.RootFolder is null ? null : Path.Combine(version.RootFolder.Path, version.FolderName);
        var tags = version.Tags.Count == 0 ? null : version.Tags.OrderBy(t => t.Id).Select(t => new TagRef(t.Id, t.Label)).ToList();
        var filePath = folder is null || version.File is null ? null : Path.Combine(folder, version.File.RelativePath);
        return new VersionRef(version.Id, version.Name, version.Mode, version.IsDefault, version.Monitored, version.QualityProfile?.Name, folder, tags, filePath, version.File?.Size);
    }
}

/// <param name="QualityVersion">The quality's revision: 2 for a PROPER or a REPACK.</param>
/// <param name="Languages">Language names as the parser reads them ("English", "French").</param>
/// <param name="CustomFormats">The release's custom formats, judged against the version's profile; ids in <paramref name="CustomFormatIds"/>, same order.</param>
public sealed record ReleaseRef(
    string Title,
    string? Quality,
    string? Indexer,
    long Size,
    string? ReleaseGroup = null,
    int? Seeders = null,
    string? InfoHash = null,
    int? QualityVersion = null,
    List<string>? Languages = null,
    IndexerFlags? IndexerFlags = null,
    List<string>? CustomFormats = null,
    List<int>? CustomFormatIds = null,
    int? CustomFormatScore = null);

/// <param name="CustomFormatIds">Ids of <paramref name="CustomFormats"/>, same order.</param>
/// <param name="SceneName">The release name the file came from.</param>
/// <param name="SourcePath">On an import: where the file was taken from.</param>
/// <param name="RecycleBinPath">On a replaced or deleted file: where the recycle bin keeps it.</param>
public sealed record FileRef(
    int Id,
    string Path,
    string? Quality,
    long Size,
    List<string>? CustomFormats = null,
    int? CustomFormatScore = null,
    string? RelativePath = null,
    int? QualityVersion = null,
    string? ReleaseGroup = null,
    string? SceneName = null,
    List<string>? Languages = null,
    IndexerFlags? IndexerFlags = null,
    MediaInfoRef? MediaInfo = null,
    DateTime? DateAdded = null,
    List<int>? CustomFormatIds = null,
    string? SourcePath = null,
    string? RecycleBinPath = null)
{
    /// <param name="path">The file's full path: the version's folder and its relative path.</param>
    public static FileRef From(MediaFile file, string path)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new FileRef(
            file.Id,
            path,
            file.Quality?.Name,
            file.Size,
            RelativePath: file.RelativePath,
            QualityVersion: file.Revision.Version,
            ReleaseGroup: file.ReleaseGroup,
            SceneName: file.OriginalReleaseTitle,
            Languages: [.. file.Languages],
            IndexerFlags: file.IndexerFlags,
            MediaInfo: file.MediaInfo is null ? null : MediaInfoRef.From(file.MediaInfo),
            DateAdded: file.DateAdded);
    }
}

/// <summary>What ffprobe found, the part Radarr's webhook sends.</summary>
public sealed record MediaInfoRef(
    double? AudioChannels,
    string? AudioCodec,
    List<string> AudioLanguages,
    int? Width,
    int? Height,
    List<string> Subtitles,
    string? VideoCodec,
    string? VideoDynamicRange,
    string? VideoDynamicRangeType)
{
    public static MediaInfoRef From(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new MediaInfoRef(info.AudioChannels, info.AudioCodec, [.. info.AudioLanguages], info.Width, info.Height, [.. info.Subtitles], info.VideoCodec, info.VideoDynamicRange, info.VideoDynamicRangeType);
    }
}

public sealed record MovieAdded(MovieRef Movie, VersionRef Version) : DomainEvent;

public sealed record VersionAdded(MovieRef Movie, VersionRef Version) : DomainEvent;

/// <summary>Monitoring, profile, availability, mode, name, constraints or tags of a version changed; its file is untouched.</summary>
public sealed record VersionUpdated(MovieRef Movie, VersionRef Version) : DomainEvent;

/// <param name="Versions">Every version the film had, so a consumer that tracked them (the compat SignalR hub) can drop each one.</param>
public sealed record MovieDeleted(MovieRef Movie, bool DeletedFiles, List<VersionRef>? Versions = null) : DomainEvent;

public sealed record VersionDeleted(MovieRef Movie, VersionRef Version, bool DeletedFiles) : DomainEvent;

/// <param name="DownloadClientType">The client's implementation: <c>QBittorrent</c>.</param>
public sealed record ReleaseGrabbed(MovieRef Movie, VersionRef Version, ReleaseRef Release, int GrabId, string DownloadClient, string DownloadId, string? Trigger, string? DownloadClientType = null) : DomainEvent;

/// <param name="DownloadClient">Name of the client the file was downloaded with; null for a file imported from a folder.</param>
public sealed record FileImported(
    MovieRef Movie,
    VersionRef Version,
    FileRef File,
    ReleaseRef? Release,
    int? GrabId,
    bool Manual,
    string Transfer,
    string? DownloadClient = null,
    string? DownloadClientType = null,
    string? DownloadId = null) : DomainEvent;

/// <param name="PreviousFile">The replaced file, with the recycle bin path when it went there.</param>
public sealed record FileUpgraded(
    MovieRef Movie,
    VersionRef Version,
    FileRef File,
    FileRef PreviousFile,
    ReleaseRef? Release,
    int? GrabId,
    bool Manual,
    string Transfer,
    string? DownloadClient = null,
    string? DownloadClientType = null,
    string? DownloadId = null) : DomainEvent;

public sealed record DownloadFailed(MovieRef? Movie, VersionRef? Version, ReleaseRef Release, int GrabId, string Reason, bool Blocklisted, bool SearchRequested) : DomainEvent;

public sealed record ImportFailed(
    MovieRef? Movie,
    VersionRef? Version,
    ReleaseRef Release,
    int GrabId,
    string Reason,
    string? DownloadClient = null,
    string? DownloadClientType = null,
    string? DownloadId = null) : DomainEvent;

public sealed record FileDeleted(MovieRef Movie, VersionRef Version, FileRef File, string Reason) : DomainEvent;

/// <summary>A file was moved to the current naming template; <paramref name="File"/> carries the new path.</summary>
public sealed record FileRenamed(MovieRef Movie, VersionRef Version, FileRef File, string PreviousPath) : DomainEvent;

public sealed record DefaultVersionChanged(MovieRef Movie, VersionRef NewDefault, VersionRef? PreviousDefault) : DomainEvent;

public enum HealthIssueType
{
    Notice = 1,
    Warning = 2,
    Error = 3,
}

public sealed record HealthIssueRaised(string Source, HealthIssueType IssueType, string Message) : DomainEvent;

/// <param name="IssueType">The level the issue had; Radarr's HealthRestored webhook carries it.</param>
public sealed record HealthIssueResolved(string Source, string Message, HealthIssueType? IssueType = null) : DomainEvent;

/// <summary>Sent by the "test" button of a notification endpoint.</summary>
public sealed record TestEvent(string Message) : DomainEvent;

/// <summary>Every event a consumer can subscribe to, with the wording the settings page shows.</summary>
public static class DomainEventCatalog
{
    public static readonly IReadOnlyList<(Type Clr, string Type, string Description)> Entries =
    [
        (typeof(MovieAdded), "movieAdded", "A film was added to the library"),
        (typeof(VersionAdded), "versionAdded", "A version was added to a film"),
        (typeof(VersionUpdated), "versionUpdated", "A version's settings changed"),
        (typeof(MovieDeleted), "movieDeleted", "A film was removed from the library"),
        (typeof(VersionDeleted), "versionDeleted", "A version was removed from a film"),
        (typeof(ReleaseGrabbed), "grabbed", "A release was sent to a download client"),
        (typeof(FileImported), "imported", "A file landed in the library"),
        (typeof(FileUpgraded), "upgraded", "A file replaced a lower one"),
        (typeof(DownloadFailed), "downloadFailed", "A download failed and was blocklisted"),
        (typeof(ImportFailed), "importFailed", "A completed download could not be imported"),
        (typeof(FileDeleted), "fileDeleted", "A file was deleted"),
        (typeof(FileRenamed), "fileRenamed", "A file was renamed"),
        (typeof(DefaultVersionChanged), "defaultVersionChanged", "The default version of a film changed"),
        (typeof(SeriesAdded), "seriesAdded", "A series was added to the library"),
        (typeof(SeriesDeleted), "seriesDeleted", "A series was removed from the library"),
        (typeof(SeriesVersionDeleted), "seriesVersionDeleted", "A version was removed from a series"),
        (typeof(EpisodeGrabbed), "episodeGrabbed", "A release of episodes was sent to a download client"),
        (typeof(EpisodeFileImported), "episodeImported", "An episode file landed in the library (or replaced a lower one)"),
        (typeof(EpisodeFileRenamed), "episodeFileRenamed", "Episode files were renamed"),
        (typeof(EpisodeFileDeleted), "episodeFileDeleted", "An episode file was deleted"),
        (typeof(EpisodeDownloadFailed), "episodeDownloadFailed", "A download of episodes failed and was blocklisted"),
        (typeof(EpisodeImportFailed), "episodeImportFailed", "A completed download of episodes could not be imported"),
        (typeof(HealthIssueRaised), "healthIssue", "A health check raised an issue"),
        (typeof(HealthIssueResolved), "healthRestored", "A health issue went away"),
        (typeof(TestEvent), "test", "Test notification"),
    ];

    private static readonly Dictionary<Type, string> Names = Entries.ToDictionary(e => e.Clr, e => e.Type);

    public static IReadOnlyList<(string Type, string Description)> All { get; } = Entries.Select(e => (e.Type, e.Description)).ToList();

    public static bool IsKnown(string type) => Names.Values.Contains(type, StringComparer.Ordinal);

    public static string NameOf(Type clr) =>
        Names.TryGetValue(clr, out var name) ? name : throw new InvalidOperationException($"{clr.Name} is not listed in the event catalogue.");
}
