using Bigarr.Api.Infrastructure.Indexers.Torznab;
using Bigarr.Parsing.Episodes;
using Bigarr.Parsing.Episodes.Model;

namespace Bigarr.Api.Infrastructure.Tv;

/// <summary>A series of the library as the lookup sees it: its titles, year, TVDB id and IMDb id.</summary>
public sealed record IndexedSeries(int Id, int? TvdbId, string Title, int? Year, IReadOnlyList<string> AlternateTitles, string? ImdbId = null);

/// <summary>An XEM scene name of a show (by TVDB id), with the scene season it stands for (null: every season).</summary>
public sealed record SceneAlias(int TvdbId, string Title, int? SceneSeasonNumber);

public enum SeriesMatchKind
{
    None = 0,

    /// <summary>By the release's TVDB id, once no title matched.</summary>
    Id = 1,
    Title = 2,
    SceneName = 3,
    TitleAndYear = 4,

    /// <summary>By the release's IMDb id, once no title matched and the release carried no TVDB id.</summary>
    ImdbId = 5,
}

/// <summary>The outcome of a series lookup. <see cref="Ambiguous"/> lists the candidates when more than one fits.</summary>
public sealed record SeriesMatch(IndexedSeries? Series, SeriesMatchKind Kind, int? SceneSeasonNumber, IReadOnlyList<IndexedSeries> Ambiguous)
{
    public static readonly SeriesMatch NotFound = new(null, SeriesMatchKind.None, null, []);

    public bool IsAmbiguous => Ambiguous.Count > 1;

    /// <summary>Found by an id rather than a name: the release's title is an alias the library does not know.</summary>
    public bool IsById => Kind is SeriesMatchKind.Id or SeriesMatchKind.ImdbId;
}

/// <summary>
/// Finds the library series a parsed title stands for, in Sonarr's order (<c>ParsingService.FindSeries</c>): an XEM
/// scene name, the cleaned title or an alternate title, title + year, then the release's TVDB id and, when the release
/// carries no TVDB id, its IMDb id. Titles are compared after the episode parser's <c>CleanSeriesTitle</c>. Two series
/// answering the same title are reported, never picked, unless the release's id names one of them.
/// </summary>
public sealed class SeriesTitleIndex
{
    private readonly IReadOnlyList<IndexedSeries> _series;
    private readonly Dictionary<int, IndexedSeries> _byTvdb;
    private readonly Dictionary<string, IndexedSeries> _byImdb;
    private readonly ILookup<string, IndexedSeries> _byTitle;
    private readonly ILookup<string, SceneAlias> _scene;

    public SeriesTitleIndex(IEnumerable<IndexedSeries> series, IEnumerable<SceneAlias> sceneNames)
    {
        _series = series.ToList();
        _byTvdb = _series.Where(s => s.TvdbId is not null).GroupBy(s => s.TvdbId!.Value).ToDictionary(g => g.Key, g => g.First());
        // Like Sonarr's FindByImdbId (SingleOrDefault): an IMDb id two series share points to neither.
        _byImdb = _series
            .Select(s => (Key: TorznabParser.NormalizeImdbId(s.ImdbId), Series: s))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Series, StringComparer.Ordinal);
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

    /// <summary>The one library series with this IMDb id (<c>tt0133093</c>, <c>133093</c>…); null when none or several.</summary>
    public IndexedSeries? ByImdbId(string? imdbId) => TorznabParser.NormalizeImdbId(imdbId) is { } key ? _byImdb.GetValueOrDefault(key) : null;

    /// <summary>The scene season a title designates through XEM ("Show S2" → 2), when it is a scene name of that show.</summary>
    public int? SceneSeasonFor(string? title, int? tvdbId)
    {
        if (tvdbId is null)
        {
            return null;
        }

        return _scene[Clean(title)].Where(n => n.TvdbId == tvdbId).Select(n => n.SceneSeasonNumber).FirstOrDefault(s => s is not null);
    }

    /// <summary>
    /// Sonarr's <c>FindByTitleInexact</c>: the library series whose cleaned title (or alternate or XEM name) starts the
    /// cleaned release name, the longest one; null when none does or two series tie. For names the parser cannot read.
    /// </summary>
    public IndexedSeries? FindInexact(string releaseTitle)
    {
        var clean = Clean(releaseTitle);
        if (clean.Length == 0)
        {
            return null;
        }

        var hits = _byTitle.Where(g => clean.StartsWith(g.Key, StringComparison.Ordinal)).SelectMany(g => g.Select(series => (g.Key, Series: series)))
            .Concat(_scene.Where(g => clean.StartsWith(g.Key, StringComparison.Ordinal)).SelectMany(g => g.Select(n => (g.Key, Series: ByTvdbId(n.TvdbId)))).Where(h => h.Series is not null).Select(h => (h.Key, Series: h.Series!)))
            .ToList();
        if (hits.Count == 0)
        {
            return null;
        }

        var longest = hits.Max(h => h.Key.Length);
        var best = hits.Where(h => h.Key.Length == longest).Select(h => h.Series).Distinct().ToList();
        return best.Count == 1 ? best[0] : null;
    }

    /// <param name="tvdbId">The release's TVDB id, if it carries one.</param>
    /// <param name="imdbId">The release's IMDb id, if it carries one: only consulted when <paramref name="tvdbId"/> is null.</param>
    public SeriesMatch Find(ParsedEpisodeInfo parsed, int? tvdbId = null, string? imdbId = null)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        return Find(parsed.SeriesTitle, parsed.SeriesTitleInfo?.TitleWithoutYear, parsed.SeriesTitleInfo?.Year is > 0 ? parsed.SeriesTitleInfo.Year : null, tvdbId, imdbId);
    }

    public SeriesMatch Find(string? title, string? titleWithoutYear, int? year, int? tvdbId = null, string? imdbId = null)
    {
        var byName = FindByName(title, titleWithoutYear, year);
        if (byName.Series is not null)
        {
            return byName;
        }

        // Sonarr only falls back to the ids once no name matched; an id never overrides a name that found another show.
        if (tvdbId is > 0)
        {
            return ByTvdbId(tvdbId.Value) is { } byTvdb && Fits(byName, byTvdb)
                ? new SeriesMatch(byTvdb, SeriesMatchKind.Id, SceneSeasonFor(title, byTvdb.TvdbId), [])
                : byName;
        }

        if (ByImdbId(imdbId) is { } byImdb && Fits(byName, byImdb))
        {
            return new SeriesMatch(byImdb, SeriesMatchKind.ImdbId, SceneSeasonFor(title, byImdb.TvdbId), []);
        }

        return byName;
    }

    /// <summary>An id match stands when no name matched, or picks one of the series a name matched several of.</summary>
    private static bool Fits(SeriesMatch byName, IndexedSeries byId) => !byName.IsAmbiguous || byName.Ambiguous.Contains(byId);

    private SeriesMatch FindByName(string? title, string? titleWithoutYear, int? year)
    {
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
