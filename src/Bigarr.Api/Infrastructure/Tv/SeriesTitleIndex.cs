using Bigarr.Parsing.Episodes;
using Bigarr.Parsing.Episodes.Model;

namespace Bigarr.Api.Infrastructure.Tv;

/// <summary>A series of the library as the lookup sees it: its titles, year and TVDB id.</summary>
public sealed record IndexedSeries(int Id, int? TvdbId, string Title, int? Year, IReadOnlyList<string> AlternateTitles);

/// <summary>An XEM scene name of a show (by TVDB id), with the scene season it stands for (null: every season).</summary>
public sealed record SceneAlias(int TvdbId, string Title, int? SceneSeasonNumber);

public enum SeriesMatchKind
{
    None = 0,
    Id = 1,
    Title = 2,
    SceneName = 3,
    TitleAndYear = 4,
}

/// <summary>The outcome of a series lookup. <see cref="Ambiguous"/> lists the candidates when more than one fits.</summary>
public sealed record SeriesMatch(IndexedSeries? Series, SeriesMatchKind Kind, int? SceneSeasonNumber, IReadOnlyList<IndexedSeries> Ambiguous)
{
    public static readonly SeriesMatch NotFound = new(null, SeriesMatchKind.None, null, []);

    public bool IsAmbiguous => Ambiguous.Count > 1;
}

/// <summary>
/// Finds the library series a parsed title stands for, in Sonarr's order (<c>ParsingService.FindSeries</c>): an explicit
/// TVDB id, an XEM scene name, the cleaned title or an alternate title, then title + year. Titles are compared after
/// the episode parser's <c>CleanSeriesTitle</c>. Two series answering the same title are reported, never picked.
/// </summary>
public sealed class SeriesTitleIndex
{
    private readonly IReadOnlyList<IndexedSeries> _series;
    private readonly Dictionary<int, IndexedSeries> _byTvdb;
    private readonly ILookup<string, IndexedSeries> _byTitle;
    private readonly ILookup<string, SceneAlias> _scene;

    public SeriesTitleIndex(IEnumerable<IndexedSeries> series, IEnumerable<SceneAlias> sceneNames)
    {
        _series = series.ToList();
        _byTvdb = _series.Where(s => s.TvdbId is not null).GroupBy(s => s.TvdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        _byTitle = _series
            .SelectMany(s => new[] { s.Title }.Concat(s.AlternateTitles).Select(t => (Key: Clean(t), Series: s)))
            .Where(x => x.Key.Length > 0)
            .Distinct()
            .ToLookup(x => x.Key, x => x.Series);
        _scene = sceneNames.Select(n => (Key: Clean(n.Title), Name: n)).Where(x => x.Key.Length > 0).ToLookup(x => x.Key, x => x.Name);
    }

    public IReadOnlyList<IndexedSeries> All => _series;

    public static string Clean(string? title) => string.IsNullOrWhiteSpace(title) ? string.Empty : title.CleanSeriesTitle();

    public IndexedSeries? ByTvdbId(int tvdbId) => _byTvdb.GetValueOrDefault(tvdbId);

    /// <summary>The scene season a title designates through XEM ("Show S2" → 2), when it is a scene name of that show.</summary>
    public int? SceneSeasonFor(string? title, int? tvdbId)
    {
        if (tvdbId is null)
        {
            return null;
        }

        return _scene[Clean(title)].Where(n => n.TvdbId == tvdbId).Select(n => n.SceneSeasonNumber).FirstOrDefault(s => s is not null);
    }

    public SeriesMatch Find(ParsedEpisodeInfo parsed, int? tvdbId = null)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return Find(parsed.SeriesTitle, parsed.SeriesTitleInfo?.TitleWithoutYear, parsed.SeriesTitleInfo?.Year is > 0 ? parsed.SeriesTitleInfo.Year : null, tvdbId);
    }

    public SeriesMatch Find(string? title, string? titleWithoutYear, int? year, int? tvdbId = null)
    {
        if (tvdbId is > 0 && ByTvdbId(tvdbId.Value) is { } byId)
        {
            return new SeriesMatch(byId, SeriesMatchKind.Id, SceneSeasonFor(title, byId.TvdbId), []);
        }

        var clean = Clean(title);
        if (clean.Length == 0)
        {
            return SeriesMatch.NotFound;
        }

        // XEM names first, like Sonarr's scene mappings: they are the scene's own spelling, season included.
        var aliases = _scene[clean].ToList();
        var aliasShows = aliases.Select(a => a.TvdbId).Distinct().ToList();
        if (aliasShows.Count == 1 && ByTvdbId(aliasShows[0]) is { } byAlias)
        {
            var season = aliases.Select(a => a.SceneSeasonNumber).FirstOrDefault(s => s is not null);
            return new SeriesMatch(byAlias, SeriesMatchKind.SceneName, season, []);
        }

        var byTitle = _byTitle[clean].Distinct().ToList();
        if (byTitle.Count > 1 && year is not null)
        {
            byTitle = byTitle.Where(s => s.Year == year).ToList();
        }

        if (byTitle.Count == 1)
        {
            return new SeriesMatch(byTitle[0], SeriesMatchKind.Title, null, []);
        }

        if (byTitle.Count > 1)
        {
            return new SeriesMatch(null, SeriesMatchKind.None, null, byTitle);
        }

        if (year is not null)
        {
            var withoutYear = Clean(titleWithoutYear);
            var byYear = _byTitle[withoutYear].Distinct().Where(s => s.Year == year).ToList();
            if (byYear.Count == 1)
            {
                return new SeriesMatch(byYear[0], SeriesMatchKind.TitleAndYear, null, []);
            }

            if (byYear.Count > 1)
            {
                return new SeriesMatch(null, SeriesMatchKind.None, null, byYear);
            }
        }

        return SeriesMatch.NotFound;
    }
}
