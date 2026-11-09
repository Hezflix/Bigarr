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

        if (series.SeriesType == SeriesType.Anime && episode.SeasonNumber > 0)
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
        if (series.SeriesType == SeriesType.Anime)
        {
            foreach (var name in seasons.SelectMany(s => SeasonNames(series, sceneNames, s)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                criteria.Add(new SeriesSearchCriteria(name, null, null, null, SeriesType.Anime, []));
            }
        }

        return criteria;
    }

    public static List<SeriesSearchCriteria> ForSeries(Series series, IReadOnlyList<SceneAlias> sceneNames) => [Base(series, sceneNames, null)];

    /// <summary>The user's own words: no ids, no numbers, every indexer gets a text query.</summary>
    public static List<SeriesSearchCriteria> ForText(Series series, string query) =>
        [new SeriesSearchCriteria(query, null, null, null, series.SeriesType, [])];

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
        series.UseSceneNumbering && series.Ordering == EpisodeOrder.Tvdb && episode.SceneSeasonNumber is not null;
}
