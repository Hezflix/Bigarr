using Bigarr.Api.Infrastructure.Tv;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Features.Search;

using Series = Bigarr.Domain.Tv.Series;

/// <summary>
/// What to ask the indexers for an episode, a season or a whole series, the way Sonarr's <c>ReleaseSearchService</c>
/// builds its criteria: XEM scene numbers when the show is scene-mapped (TVDB order only), the air date for a daily
/// show, the absolute number for an anime (also under the XEM name of its scene season: "Show S2 05"), and the series'
/// scene names and alternate titles for the text queries.
/// </summary>
public static class SeriesSearchCriteriaBuilder
{
    public static List<SeriesSearchCriteria> ForEpisode(Series series, Episode episode, IReadOnlyList<SceneAlias> sceneNames)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);

        var scene = UsesScene(series, episode);
        var season = scene ? episode.SceneSeasonNumber!.Value : episode.SeasonNumber;
        var number = scene ? episode.SceneEpisodeNumber ?? episode.EpisodeNumber : episode.EpisodeNumber;
        var baseCriteria = Base(series, sceneNames, season);

        if (series.SeriesType == SeriesType.Daily && episode.AirDate is { } airDate && episode.SeasonNumber > 0)
        {
            return [baseCriteria with { AirDate = airDate }];
        }

        // Sonarr: a special is searched by its title too (an anime special only when it has no absolute number).
        var animeAbsolute = series.SeriesType == SeriesType.Anime && (episode.AbsoluteEpisodeNumber is not null || episode.SceneAbsoluteEpisodeNumber is not null);
        if (episode.SeasonNumber == 0 && !animeAbsolute)
        {
            return
            [
                Special(series, sceneNames, [episode]),
                baseCriteria with { SeasonNumber = season, EpisodeNumber = number },
            ];
        }

        if (series.SeriesType == SeriesType.Anime && (episode.SeasonNumber > 0 || animeAbsolute))
        {
            var absolute = (scene ? episode.SceneAbsoluteEpisodeNumber : null) ?? episode.AbsoluteEpisodeNumber;
            var criteria = new List<SeriesSearchCriteria>
            {
                baseCriteria with { SeasonNumber = season, EpisodeNumber = number, AbsoluteEpisodeNumber = absolute },
            };

            // An XEM name standing for this scene season numbers its episodes from 1: "Sousou no Frieren S2 - 05".
            foreach (var name in SeasonNames(series, sceneNames, season))
            {
                criteria.Add(new SeriesSearchCriteria(name, null, null, null, SeriesType.Anime, []) { AbsoluteEpisodeNumber = number });
            }

            return criteria;
        }

        return [baseCriteria with { SeasonNumber = season, EpisodeNumber = number }];
    }

    /// <summary>One season request per scene season the episodes fall in (usually one), plus the XEM season names for anime.</summary>
    public static List<SeriesSearchCriteria> ForSeason(Series series, int seasonNumber, IReadOnlyCollection<Episode> seasonEpisodes, IReadOnlyList<SceneAlias> sceneNames)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(seasonEpisodes);

        var seasons = seasonEpisodes.Select(e => UsesScene(series, e) ? e.SceneSeasonNumber!.Value : e.SeasonNumber).Distinct().Order().ToList();
        if (seasons.Count == 0)
        {
            seasons.Add(seasonNumber);
        }

        var criteria = seasons.Select(s => Base(series, sceneNames, s) with { SeasonNumber = s }).ToList();
        if (seasonNumber == 0)
        {
            // Specials: Sonarr searches each one by title as well.
            criteria.Insert(0, Special(series, [], seasonEpisodes.OrderBy(e => e.EpisodeNumber).Take(MaxSpecialTitles).ToList()));
            return criteria;
        }

        if (series.SeriesType == SeriesType.Anime)
        {
            foreach (var name in seasons.SelectMany(s => SeasonNames(series, sceneNames, s)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                criteria.Add(new SeriesSearchCriteria(name, null, null, null, SeriesType.Anime, []));
            }
        }

        return criteria;
    }

    /// <summary>
    /// The fallback on the series' alternative order: the episode asked by its numbers in that order (no
    /// XEM: scene numbers are TVDB's), its absolute number in the order for an anime. Empty when the order lacks it.
    /// </summary>
    public static List<SeriesSearchCriteria> ForOrderEpisode(Series series, Episode episode, IReadOnlyList<SceneAlias> sceneNames)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episode);
        if (series.Ordering == EpisodeOrder.Tvdb || !episode.InOrder || series.SeriesType == SeriesType.Daily)
        {
            return [];
        }

        var season = episode.OrderSeasonNumber!.Value;
        var criteria = Base(series, sceneNames, null) with { SeasonNumber = season, EpisodeNumber = episode.OrderEpisodeNumber!.Value };
        return series.SeriesType == SeriesType.Anime && episode.OrderAbsoluteNumber is { } absolute
            ? [criteria with { AbsoluteEpisodeNumber = absolute }]
            : [criteria];
    }

    /// <summary>The seasons of the alternative order the episodes fall in, one request each.</summary>
    public static List<SeriesSearchCriteria> ForOrderSeasons(Series series, IReadOnlyCollection<Episode> episodes, IReadOnlyList<SceneAlias> sceneNames)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);
        if (series.Ordering == EpisodeOrder.Tvdb || series.SeriesType == SeriesType.Daily)
        {
            return [];
        }

        return episodes.Where(e => e.InOrder && e.OrderSeasonNumber > 0).Select(e => e.OrderSeasonNumber!.Value).Distinct().Order()
            .Select(s => Base(series, sceneNames, null) with { SeasonNumber = s })
            .ToList();
    }

    public static List<SeriesSearchCriteria> ForSeries(Series series, IReadOnlyList<SceneAlias> sceneNames) => [Base(series, sceneNames, null)];

    /// <summary>The user's own words: no ids, no numbers, every indexer gets a text query.</summary>
    public static List<SeriesSearchCriteria> ForText(Series series, string query) =>
        [new SeriesSearchCriteria(query, null, null, null, series.SeriesType, [])];

    /// <summary>Specials searched by title in a season search: one request each, so a long list is cut.</summary>
    public const int MaxSpecialTitles = 30;

    /// <summary>
    /// Sonarr's special search: "&lt;scene title&gt; &lt;episode title&gt;" for the series title and its XEM names. In a
    /// season search (several specials) only the series title, one query per special.
    /// </summary>
    private static SeriesSearchCriteria Special(Series series, IReadOnlyList<SceneAlias> sceneNames, IReadOnlyList<Episode> specials)
    {
        var titles = new[] { series.Title }.Concat(sceneNames.Select(n => n.Title)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        var queries = specials.Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .SelectMany(e => titles.Select(t => $"{t} {e.Title}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SeriesSearchCriteria(series.Title, null, null, null, series.SeriesType, []) { SpecialQueries = queries };
    }

    private static SeriesSearchCriteria Base(Series series, IReadOnlyList<SceneAlias> sceneNames, int? season)
    {
        var alternates = sceneNames
            .Where(n => n.SceneSeasonNumber is null || n.SceneSeasonNumber == season)
            .Select(n => n.Title)
            .Concat(new[] { series.OriginalTitle }.Concat(series.AlternativeTitles.Select(a => a.Title)).OfType<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t) && !string.Equals(t, series.Title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SeriesSearchCriteria(series.Title, series.TvdbId, series.TmdbId, series.ImdbId, series.SeriesType, alternates);
    }

    private static IEnumerable<string> SeasonNames(Series series, IReadOnlyList<SceneAlias> sceneNames, int season) =>
        series.UseSceneNumbering
            ? sceneNames.Where(n => n.SceneSeasonNumber == season && season > 1).Select(n => n.Title).Distinct(StringComparer.OrdinalIgnoreCase)
            : [];

    private static bool UsesScene(Series series, Episode episode) =>
        series.UseSceneNumbering && episode.SceneSeasonNumber is not null;
}
