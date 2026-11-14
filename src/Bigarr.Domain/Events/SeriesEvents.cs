using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Events;

/// <summary>A show at the moment of the event: what Sonarr's webhook <c>series</c> carries, from Bigarr's own fields.</summary>
/// <param name="Type">Standard, daily or anime, as Sonarr names its series types.</param>
public sealed record SeriesRef(
    int Id,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    string Title,
    int? Year,
    SeriesType Type,
    string? Overview = null,
    List<string>? Genres = null,
    List<ImageRef>? Images = null,
    string? OriginalLanguage = null,
    string? OriginalCountry = null,
    string? Network = null)
{
    public static SeriesRef From(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return new SeriesRef(
            series.Id,
            series.TvdbId,
            series.TmdbId,
            series.ImdbId,
            series.Title,
            series.Year,
            series.SeriesType,
            series.Overview,
            series.Genres.Count == 0 ? null : [.. series.Genres],
            series.Images.Count == 0 ? null : series.Images.Select(i => new ImageRef(i.Type, i.RemoteUrl)).ToList(),
            series.OriginalLanguage,
            series.OriginalCountry,
            series.Network);
    }
}

/// <summary>One version of a show: Sonarr's <c>series</c> in the Sonarr format (its id is the version's, like the compat line).</summary>
/// <param name="Folder">The version's folder on disk, when the root folder was loaded with it.</param>
public sealed record SeriesVersionRef(
    int Id,
    string Name,
    bool IsDefault,
    bool Monitored,
    string? QualityProfile,
    string? Folder = null,
    List<TagRef>? Tags = null)
{
    public static SeriesVersionRef From(SeriesVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var folder = version.RootFolder is null ? null : Path.Combine(version.RootFolder.Path, version.FolderName);
        var tags = version.Tags.Count == 0 ? null : version.Tags.OrderBy(t => t.Id).Select(t => new TagRef(t.Id, t.Label)).ToList();
        return new SeriesVersionRef(version.Id, version.Name, version.IsDefault, version.Monitored, version.QualityProfile?.Name, folder, tags);
    }
}

/// <summary>An episode as Sonarr's webhook describes it, in TVDB numbers.</summary>
public sealed record EpisodeRef(
    int Id,
    int SeasonNumber,
    int EpisodeNumber,
    string Title,
    int? AbsoluteEpisodeNumber = null,
    DateOnly? AirDate = null,
    DateTime? AirDateUtc = null,
    int? TvdbId = null,
    string? Overview = null)
{
    public static EpisodeRef From(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new EpisodeRef(episode.Id, episode.SeasonNumber, episode.EpisodeNumber, episode.Title, episode.AbsoluteEpisodeNumber, episode.AirDate, episode.AirDateUtc, episode.TvdbId, episode.Overview);
    }

    public static List<EpisodeRef> From(IEnumerable<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        return episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).Select(From).ToList();
    }
}

/// <summary>A renamed episode file: its new snapshot and where it was.</summary>
public sealed record RenamedFileRef(FileRef File, string PreviousPath, string? PreviousRelativePath = null);

public sealed record SeriesAdded(SeriesRef Series, SeriesVersionRef Version) : DomainEvent;

/// <param name="Versions">Every version the show had, so a consumer that tracked them can drop each one.</param>
public sealed record SeriesDeleted(SeriesRef Series, bool DeletedFiles, List<SeriesVersionRef>? Versions = null) : DomainEvent;

public sealed record SeriesVersionDeleted(SeriesRef Series, SeriesVersionRef Version, bool DeletedFiles) : DomainEvent;

/// <summary>A version was added to a show already in the library (the first one comes with <see cref="SeriesAdded"/>).</summary>
public sealed record SeriesVersionAdded(SeriesRef Series, SeriesVersionRef Version) : DomainEvent;

/// <summary>A version's settings or monitoring changed.</summary>
/// <param name="EpisodesChanged">Season or episode monitoring changed (Sonarr's <c>episodesChanged</c>): a consumer that mirrors episodes reads them again.</param>
public sealed record SeriesVersionUpdated(SeriesRef Series, SeriesVersionRef Version, bool EpisodesChanged = false) : DomainEvent;

public sealed record SeriesDefaultVersionChanged(SeriesRef Series, SeriesVersionRef NewDefault, SeriesVersionRef? PreviousDefault) : DomainEvent;

/// <param name="DownloadClientType">The client's implementation: <c>QBittorrent</c>.</param>
public sealed record EpisodeGrabbed(
    SeriesRef Series,
    SeriesVersionRef Version,
    List<EpisodeRef> Episodes,
    ReleaseRef Release,
    int GrabId,
    string DownloadClient,
    string DownloadId,
    string? Trigger,
    string? DownloadClientType = null) : DomainEvent;

/// <summary>One episode file landed in the library (a pack makes one per file, like Sonarr's Download event).</summary>
/// <param name="PreviousFiles">The files it replaced (an upgrade), with the recycle bin path when they went there.</param>
public sealed record EpisodeFileImported(
    SeriesRef Series,
    SeriesVersionRef Version,
    List<EpisodeRef> Episodes,
    FileRef File,
    List<FileRef>? PreviousFiles,
    ReleaseRef? Release,
    int? GrabId,
    bool Manual,
    string Transfer,
    string? DownloadClient = null,
    string? DownloadClientType = null,
    string? DownloadId = null) : DomainEvent
{
    public bool IsUpgrade => PreviousFiles is { Count: > 0 };
}

public sealed record EpisodeFileRenamed(SeriesRef Series, SeriesVersionRef Version, List<RenamedFileRef> Files) : DomainEvent;

public sealed record EpisodeFileDeleted(SeriesRef Series, SeriesVersionRef Version, List<EpisodeRef> Episodes, FileRef File, string Reason) : DomainEvent;

public sealed record EpisodeDownloadFailed(
    SeriesRef? Series,
    SeriesVersionRef? Version,
    List<EpisodeRef> Episodes,
    ReleaseRef Release,
    int GrabId,
    string Reason,
    bool Blocklisted,
    bool SearchRequested) : DomainEvent;

public sealed record EpisodeImportFailed(
    SeriesRef? Series,
    SeriesVersionRef? Version,
    List<EpisodeRef> Episodes,
    ReleaseRef Release,
    int GrabId,
    string Reason,
    string? DownloadClient = null,
    string? DownloadClientType = null,
    string? DownloadId = null) : DomainEvent;
