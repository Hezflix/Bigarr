using Bigarr.Domain.Library;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Naming;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Import;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>The facts of one episode file that its name may print, whatever produced them (import, rename).</summary>
public sealed record EpisodeFileFacts(
    string? QualityName,
    int RevisionVersion,
    int RevisionReal,
    bool IsRepack,
    string? ReleaseGroup,
    IReadOnlyList<string> Languages,
    MediaInfo? MediaInfo,
    IReadOnlyList<string> CustomFormats,
    string? OriginalReleaseTitle,
    string? OriginalFileName);

/// <summary>Where an episode file goes in its version folder: the season folder and the file name of the templates.</summary>
public static class SeriesNaming
{
    public static SeriesNamingContext Context(Series series, SeriesVersion version, IEnumerable<Episode> episodes, EpisodeFileFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(episodes);

        return new SeriesNamingContext(series.Title, series.Year, series.TvdbId, series.TmdbId, series.ImdbId, version.Name)
        {
            // The series' order names the files; an episode the order lacks keeps its TVDB numbers.
            Episodes = episodes
                .OrderBy(e => e.DisplaySeasonNumber(series.Ordering)).ThenBy(e => e.DisplayEpisodeNumber(series.Ordering))
                .Select(e => new EpisodeNaming(e.DisplaySeasonNumber(series.Ordering), e.DisplayEpisodeNumber(series.Ordering), e.DisplayAbsoluteNumber(series.Ordering), e.AirDate, e.Title))
                .ToList(),
            QualityName = facts?.QualityName,
            RevisionVersion = facts?.RevisionVersion ?? 1,
            RevisionReal = facts?.RevisionReal ?? 0,
            IsRepack = facts?.IsRepack ?? false,
            ReleaseGroup = facts?.ReleaseGroup,
            Languages = facts?.Languages ?? [],
            MediaInfo = facts?.MediaInfo,
            CustomFormats = facts?.CustomFormats ?? [],
            OriginalReleaseTitle = facts?.OriginalReleaseTitle,
            OriginalFileName = facts?.OriginalFileName,
        };
    }

    /// <summary>
    /// The path relative to the version folder: <c>Season 1/Show - S01E01 - Title [Quality].mkv</c>. With renaming off
    /// the file keeps <paramref name="currentName"/>, still sorted into its season folder.
    /// </summary>
    /// <param name="rename">Overrides the "rename episodes" switch (an explicit rename always renames).</param>
    public static string RelativePath(MediaManagementConfig config, Series series, SeriesVersion version, IReadOnlyCollection<Episode> episodes, EpisodeFileFacts facts, string extension, string currentName, bool? rename = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(episodes);

        var context = Context(series, version, episodes, facts);
        var fileName = rename ?? config.RenameEpisodes
            ? SeriesNamingTemplate.RenderFile(TemplateFor(config, series), context, config.MultiEpisodeStyle, extension, config.SeriesNameCleaning) + extension
            : currentName;

        if (!version.SeasonFolder)
        {
            return fileName;
        }

        var season = episodes.Min(e => e.DisplaySeasonNumber(series.Ordering));
        var folder = SeriesNamingTemplate.RenderSeasonFolder(config.SeasonFolderTemplate, config.SpecialsFolderName, season, context, config.SeriesNameCleaning);
        return string.IsNullOrEmpty(folder) ? fileName : Path.Combine(folder, fileName);
    }

    public static string TemplateFor(MediaManagementConfig config, Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return SeriesNamingTemplate.EpisodeTemplateFor(config, series.SeriesType == SeriesType.Daily, series.SeriesType == SeriesType.Anime);
    }

    /// <summary>The folder name a new version of <paramref name="series"/> gets from the series folder template.</summary>
    public static string FolderName(MediaManagementConfig config, Series series, string versionName)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(series);
        var context = new SeriesNamingContext(series.Title, series.Year, series.TvdbId, series.TmdbId, series.ImdbId, versionName);
        var name = SeriesNamingTemplate.RenderSeriesFolder(config.SeriesFolderTemplate, context, config.SeriesNameCleaning);
        return string.IsNullOrWhiteSpace(name) || name == "_" ? FolderNameBuilder.Build(series.Title, series.Year, config.SeriesNameCleaning) : name;
    }
}
