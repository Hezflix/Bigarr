using System.Globalization;
using Bigarr.Api.Features.Compat.Radarr;
using Bigarr.Api.Features.Series;
using Bigarr.Decision;
using Bigarr.Domain.Metadata;
using Bigarr.Domain.Movies;
using Bigarr.Parsing.Episodes;

namespace Bigarr.Api.Features.Compat.Sonarr;

using Bigarr.Domain.Tv;

/// <summary>
/// Translates series lines, episodes and episode files to Sonarr's resources. Pure: everything comes in as arguments.
/// Total by design: Bazarr indexes <c>alternateTitles</c>, <c>images</c>, <c>tags</c>, <c>sortTitle</c>, <c>year</c>,
/// <c>seriesType</c>, <c>monitored</c>, <c>path</c>, <c>tvdbId</c>, <c>id</c> and <c>title</c> of every row outside
/// any error handling, so one missing key aborts its whole sync: they are always written, empty rather than absent.
/// </summary>
public static class SonarrMapper
{
    public static SonarrSeries ToSeries(SonarrRow row, IReadOnlyDictionary<(int VersionId, int Season), SeasonFigures> statistics)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(statistics);

        var (show, version) = row;
        var path = Path.Combine(version.RootFolder.Path, version.FolderName);
        var flags = version.Seasons.ToDictionary(s => s.SeasonId, s => s.Monitored);
        var seasons = show.Seasons
            .OrderBy(s => s.SeasonNumber)
            .Select(s =>
            {
                var figures = statistics.GetValueOrDefault((version.Id, s.SeasonNumber)) ?? SeasonFigures.Empty;
                return new SonarrSeason(s.SeasonNumber, flags.GetValueOrDefault(s.Id), SeasonStatistics(figures));
            })
            .ToList();

        // Sonarr's series counts leave the specials out of the episode figures; every file counts in the size.
        var regular = show.Seasons.Where(s => s.SeasonNumber > 0).Select(s => statistics.GetValueOrDefault((version.Id, s.SeasonNumber)) ?? SeasonFigures.Empty).ToList();
        var all = show.Seasons.Select(s => statistics.GetValueOrDefault((version.Id, s.SeasonNumber)) ?? SeasonFigures.Empty).ToList();
        var fileCount = regular.Sum(f => f.EpisodeFileCount);
        var episodeCount = regular.Sum(f => f.EpisodeCount);

        return Base(show) with
        {
            Id = version.Id,
            Images = Images(show, version.Id),
            Seasons = seasons,
            Path = path,
            QualityProfileId = version.QualityProfileId,
            SeasonFolder = version.SeasonFolder,
            Monitored = version.Monitored,
            MonitorNewItems = version.MonitorNewItems == NewItemMonitoring.None ? "none" : "all",
            RootFolderPath = version.RootFolder.Path,
            Folder = version.FolderName,
            Tags = version.Tags.Select(t => t.Id).Order().ToList(),
            Added = version.CreatedAt == default ? show.AddedAt : version.CreatedAt,
            Statistics = new SonarrSeriesStatistics(
                show.Seasons.Count(s => s.SeasonNumber > 0),
                fileCount,
                episodeCount,
                regular.Sum(f => f.TotalEpisodeCount),
                all.Sum(f => f.SizeOnDisk),
                all.SelectMany(f => f.ReleaseGroups).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList(),
                Percent(fileCount, episodeCount)),
        };
    }

    /// <summary>
    /// A show in the library the key's view does not see (another version's show): Sonarr's lookup shape, without
    /// <c>id</c>, which is what tells Seerr to POST. Seasons come from the library, all unmonitored.
    /// </summary>
    public static SonarrSeries ToLookup(Series show)
    {
        ArgumentNullException.ThrowIfNull(show);
        return Base(show) with
        {
            Images = RemoteImages(show.Images),
            Seasons = show.Seasons.OrderBy(s => s.SeasonNumber).Select(s => new SonarrSeason(s.SeasonNumber, false)).ToList(),
        };
    }

    /// <summary>A show from the metadata provider (not in the library): the lookup shape, without <c>id</c>.</summary>
    public static SonarrSeries ToLookup(SeriesMetadata found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var tvdbId = found.TvdbId ?? 0;
        return new SonarrSeries
        {
            Title = found.Title,
            AlternateTitles = found.AlternativeTitles.Select(t => new SonarrAlternateTitle(t.Title)).ToList(),
            SortTitle = SortTitle.Build(found.Title),
            Status = Status(found.Status),
            Ended = found.Status == SeriesStatus.Ended,
            Overview = found.Overview,
            Network = found.Network,
            AirTime = found.AirTime,
            Images = RemoteImages(found.Images),
            OriginalLanguage = RadarrMapper.OriginalLanguage(found.OriginalLanguage),
            RemotePoster = found.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
            Seasons = found.Seasons.OrderBy(s => s.SeasonNumber).Select(s => new SonarrSeason(s.SeasonNumber, false)).ToList(),
            Year = found.Year ?? 0,
            Runtime = found.Runtime ?? 0,
            TvdbId = tvdbId,
            TmdbId = found.TmdbId ?? 0,
            FirstAired = RadarrMapper.Utc(found.FirstAired),
            LastAired = RadarrMapper.Utc(found.LastAired),
            SeriesType = Type(found.SuggestedType),
            CleanTitle = found.Title.CleanSeriesTitle(),
            ImdbId = found.ImdbId,
            TitleSlug = tvdbId.ToString(CultureInfo.InvariantCulture),
            Genres = [.. found.Genres],
        };
    }

    /// <summary>A text search hit (no season list: the provider's search does not fetch episodes).</summary>
    public static SonarrSeries ToLookup(SeriesLookupResult found)
    {
        ArgumentNullException.ThrowIfNull(found);
        var tvdbId = found.TvdbId ?? 0;
        return new SonarrSeries
        {
            Title = found.Title,
            SortTitle = SortTitle.Build(found.Title),
            Status = Status(found.Status),
            Ended = found.Status == SeriesStatus.Ended,
            Overview = found.Overview,
            Network = found.Network,
            Images = found.PosterUrl is null ? [] : [new SonarrImage("poster", found.PosterUrl, found.PosterUrl)],
            RemotePoster = found.PosterUrl,
            Year = found.Year ?? 0,
            TvdbId = tvdbId,
            TmdbId = found.TmdbId ?? 0,
            SeriesType = Type(found.SuggestedType),
            CleanTitle = found.Title.CleanSeriesTitle(),
            ImdbId = found.ImdbId,
            TitleSlug = tvdbId.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static SonarrSeries Base(Series show) => new()
    {
        Title = show.Title,
        AlternateTitles = show.AlternativeTitles.Select(t => new SonarrAlternateTitle(t.Title)).ToList(),
        SortTitle = show.SortTitle,
        Status = Status(show.Status),
        Ended = show.Status == SeriesStatus.Ended,
        Overview = show.Overview,
        Network = show.Network,
        AirTime = show.AirTime,
        OriginalLanguage = RadarrMapper.OriginalLanguage(show.OriginalLanguage),
        RemotePoster = show.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
        Year = show.Year ?? 0,
        UseSceneNumbering = show.UseSceneNumbering,
        Runtime = show.Runtime ?? 0,
        TvdbId = show.TvdbId ?? 0,
        TmdbId = show.TmdbId ?? 0,
        FirstAired = RadarrMapper.Utc(show.FirstAired),
        LastAired = RadarrMapper.Utc(show.LastAired),
        SeriesType = Type(show.SeriesType),
        CleanTitle = show.Title.CleanSeriesTitle(),
        ImdbId = show.ImdbId,
        TitleSlug = (show.TvdbId ?? 0).ToString(CultureInfo.InvariantCulture),
        Genres = [.. show.Genres],
        Added = show.AddedAt,
    };

    public static SonarrSeasonStatistics SeasonStatistics(SeasonFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new SonarrSeasonStatistics(null, null, figures.EpisodeFileCount, figures.EpisodeCount, figures.TotalEpisodeCount, figures.SizeOnDisk, figures.ReleaseGroups, Percent(figures.EpisodeFileCount, figures.EpisodeCount));
    }

    private static double Percent(int files, int episodes) => episodes == 0 ? 0 : Math.Round(files * 100.0 / episodes, 2);

    /// <summary>
    /// Poster first, fanart second, with this surface's URL base as a Sonarr with a URL base writes them: Bazarr strips
    /// the base and asks <c>{base}/api/v3/MediaCover/{id}/poster-500.jpg</c>, both of which are served.
    /// </summary>
    public static List<SonarrImage> Images(Series show, int versionId)
    {
        ArgumentNullException.ThrowIfNull(show);

        var images = new List<SonarrImage>();
        if (show.Images.FirstOrDefault(i => i.Type == ImageType.Poster) is { } poster)
        {
            images.Add(new SonarrImage("poster", $"{SonarrCompat.UrlBase}/MediaCover/{versionId}/poster.jpg", poster.RemoteUrl));
        }

        if (show.Images.FirstOrDefault(i => i.Type == ImageType.Fanart) is { } fanart)
        {
            images.Add(new SonarrImage("fanart", $"{SonarrCompat.UrlBase}/MediaCover/{versionId}/fanart.jpg", fanart.RemoteUrl));
        }

        return images;
    }

    private static List<SonarrImage> RemoteImages(IEnumerable<MediaImage> images) =>
        images.GroupBy(i => i.Type).Select(g => g.First()).OrderBy(i => i.Type)
            .Select(i => new SonarrImage(i.Type == ImageType.Poster ? "poster" : "fanart", i.RemoteUrl, i.RemoteUrl))
            .ToList();

    public static SonarrEpisode ToEpisode(EpisodeTarget target, string seriesPath, bool withFile)
    {
        ArgumentNullException.ThrowIfNull(target);

        var episode = target.Episode;
        var file = target.EpisodeFile;
        return new SonarrEpisode
        {
            Id = episode.Id,
            SeriesId = target.VersionId,
            TvdbId = episode.TvdbId ?? 0,
            EpisodeFileId = file?.Id ?? 0,
            SeasonNumber = episode.SeasonNumber,
            EpisodeNumber = episode.EpisodeNumber,
            Title = string.IsNullOrEmpty(episode.Title) ? "TBA" : episode.Title,
            AirDate = episode.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AirDateUtc = episode.AirDateUtc is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null,
            Runtime = episode.Runtime ?? 0,
            Overview = episode.Overview,
            EpisodeFile = withFile && file is not null ? ToEpisodeFile(file, target.Version?.QualityProfile, seriesPath) : null,
            HasFile = file is not null,
            Monitored = target.Monitored,
            AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber,
            SceneAbsoluteEpisodeNumber = episode.SceneAbsoluteEpisodeNumber,
            SceneEpisodeNumber = episode.SceneEpisodeNumber,
            SceneSeasonNumber = episode.SceneSeasonNumber,
            UnverifiedSceneNumbering = episode.UnverifiedSceneNumbering,
            LastSearchTime = target.LastSearchAt,
            Images = episode.ImageUrl is null ? [] : [new SonarrImage("screenshot", episode.ImageUrl, episode.ImageUrl)],
        };
    }

    /// <summary>
    /// Every member Bazarr reads is set: <c>id</c>, an absolute <c>path</c> (its path mapping source), <c>size</c>, and a
    /// Sonarr-style <c>quality.quality.name</c> (<c>WEBDL-1080p</c>, split on the dash into source and resolution).
    /// </summary>
    public static SonarrEpisodeFile ToEpisodeFile(EpisodeFile file, Bigarr.Domain.Quality.QualityProfile? profile, string seriesPath)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new SonarrEpisodeFile(
            file.Id,
            file.VersionId,
            file.SeasonNumber,
            file.RelativePath,
            Path.Combine(seriesPath, file.RelativePath),
            file.Size,
            file.DateAdded,
            file.OriginalReleaseTitle,
            file.ReleaseGroup,
            RadarrMapper.Languages(file.Languages),
            RadarrMapper.ToQuality(file.Quality, file.Revision),
            [],
            0,
            (int)file.IndexerFlags,
            file.Episodes.Count > 1 ? "multiEpisode" : "singleEpisode",
            file.MediaInfo is null ? null : RadarrMapper.ToMediaInfo(file.MediaInfo),
            profile is not null && !ProfileRanking.CutoffMet(profile, file.QualityId));
    }

    public static string Status(SeriesStatus status) => status switch
    {
        SeriesStatus.Ended => "ended",
        SeriesStatus.Upcoming => "upcoming",
        SeriesStatus.Deleted => "deleted",
        _ => "continuing",
    };

    public static string Type(SeriesType type) => type switch
    {
        SeriesType.Anime => "anime",
        SeriesType.Daily => "daily",
        _ => "standard",
    };

    public static SeriesType? ParseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "anime" => SeriesType.Anime,
        "daily" => SeriesType.Daily,
        "standard" => SeriesType.Standard,
        _ => null,
    };

    public static NewItemMonitoring? ParseNewItems(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "all" => NewItemMonitoring.All,
        "none" => NewItemMonitoring.None,
        _ => null,
    };

    /// <summary>Sonarr's <c>addOptions.monitor</c>; null for absent or <c>unknown</c>, which means "follow <c>seasons[].monitored</c>".</summary>
    public static MonitoringOption? ParseMonitor(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "all" => MonitoringOption.All,
        "future" => MonitoringOption.Future,
        "missing" => MonitoringOption.Missing,
        "existing" => MonitoringOption.Existing,
        "pilot" => MonitoringOption.Pilot,
        "firstseason" => MonitoringOption.FirstSeason,
        "latestseason" or "lastseason" => MonitoringOption.LatestSeason,
        "none" => MonitoringOption.None,
        _ => null,
    };
}
