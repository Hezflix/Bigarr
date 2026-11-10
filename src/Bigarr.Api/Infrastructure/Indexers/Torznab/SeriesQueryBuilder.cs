using System.Globalization;
using Bigarr.Domain.Indexers;
using Bigarr.Domain.Tv;

namespace Bigarr.Api.Infrastructure.Indexers.Torznab;

/// <summary>One Torznab request: its parameters (<c>t</c>, <c>q</c>, ids, numbers) and the categories it asks.</summary>
public sealed record TorznabRequest(IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<int> Categories)
{
    public override string ToString() => string.Join('&', Parameters.Select(p => $"{p.Key}={p.Value}")) + $" cat={string.Join(',', Categories)}";
}

/// <summary>
/// Sonarr's <c>NewznabRequestGenerator</c> for Torznab, as request tiers: the first tier asks by ids (<c>tvdbid</c>, plus
/// <c>tmdbid</c>/<c>imdbid</c> when the indexer takes them) with the season and episode, the next one by titles
/// (series title, XEM scene names, alternate titles) and is only sent when the ids found nothing. Daily shows are
/// asked by air date (<c>season=YYYY&amp;ep=MM/DD</c>). Anime episodes are also asked as "Title 05" in the first tier,
/// the way anime groups name them. Numbers are those of the criteria: the caller puts XEM scene numbers in them.
/// </summary>
public static class SeriesQueryBuilder
{
    /// <summary>Text queries per tier: the series title and two more names at most, like the film search.</summary>
    public const int MaxTitles = 3;

    public static IReadOnlyList<IReadOnlyList<TorznabRequest>> Build(IndexerCapabilities capabilities, SeriesSearchCriteria criteria, IReadOnlyList<int> categories)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(categories);

        if (criteria.SpecialQueries.Count > 0)
        {
            // Sonarr's SpecialEpisodeSearchCriteria: one text search per "<series> <episode title>".
            return
            [
                criteria.SpecialQueries.Select(TorznabClient.CleanQuery).Where(q => q.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(q => new TorznabRequest(new Dictionary<string, string> { ["t"] = "search", ["q"] = q }, categories))
                    .ToList(),
            ];
        }

        var numbering = Numbering(criteria);
        var text = TextSuffix(criteria);
        var titles = criteria.AllTitles.Select(TorznabClient.CleanQuery).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTitles).ToList();

        var idTier = new List<TorznabRequest>();
        var ids = Ids(capabilities, criteria);
        var animeAbsolute = criteria.SeriesType == SeriesType.Anime && criteria.AbsoluteEpisodeNumber is > 0;
        var hasStandardNumbers = !animeAbsolute || (criteria.SeasonNumber is > 0 && criteria.EpisodeNumber is > 0);

        if (ids.Count > 0 && hasStandardNumbers && numbering.All(n => capabilities.SupportsTvParam(n.Key)))
        {
            var parameters = new Dictionary<string, string> { ["t"] = "tvsearch" };
            foreach (var (key, value) in ids.Concat(numbering))
            {
                parameters[key] = value;
            }

            idTier.Add(new TorznabRequest(parameters, categories));
        }

        if (animeAbsolute)
        {
            // Sonarr's anime requests: the ids with the absolute number as text, then "Title 05" per scene title.
            var absolute = criteria.AbsoluteEpisodeNumber!.Value.ToString("00", CultureInfo.InvariantCulture);
            if (ids.Count > 0 && capabilities.SupportsTvParam("q"))
            {
                var byIds = new Dictionary<string, string> { ["t"] = "tvsearch", ["q"] = absolute };
                foreach (var (key, value) in ids)
                {
                    byIds[key] = value;
                }

                idTier.Add(new TorznabRequest(byIds, categories));
            }

            foreach (var title in titles)
            {
                idTier.Add(new TorznabRequest(new Dictionary<string, string> { ["t"] = "search", ["q"] = $"{title} {absolute}" }, categories));
            }
        }

        var titleTier = new List<TorznabRequest>();
        if (!animeAbsolute || hasStandardNumbers)
        {
            var tvQuery = capabilities.SupportsTvParam("q") && numbering.All(n => capabilities.SupportsTvParam(n.Key));
            foreach (var title in titles)
            {
                Dictionary<string, string> parameters;
                if (tvQuery)
                {
                    parameters = new Dictionary<string, string> { ["t"] = "tvsearch", ["q"] = title };
                    foreach (var (key, value) in numbering)
                    {
                        parameters[key] = value;
                    }
                }
                else
                {
                    parameters = new Dictionary<string, string> { ["t"] = "search", ["q"] = text.Length == 0 ? title : $"{title} {text}" };
                }

                titleTier.Add(new TorznabRequest(parameters, categories));
            }
        }

        return new[] { idTier, titleTier }.Where(t => t.Count > 0).Select(t => (IReadOnlyList<TorznabRequest>)t).ToList();
    }

    /// <summary>The TV categories of the row, else those of the caps, else Sonarr's usual ones; anime adds its own.</summary>
    public static IReadOnlyList<int> Categories(Indexer indexer, IndexerCapabilities capabilities, SeriesType type)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(capabilities);

        IEnumerable<int> tv = indexer.TvCategories.Count > 0 ? indexer.TvCategories
            : capabilities.TvCategoryIds.Count > 0 ? capabilities.TvCategoryIds
            : Indexer.DefaultTvCategories;
        if (type == SeriesType.Anime)
        {
            IEnumerable<int> anime = indexer.AnimeCategories.Count > 0 ? indexer.AnimeCategories
                : capabilities.AnimeCategoryIds.Count > 0 ? capabilities.AnimeCategoryIds
                : [Indexer.AnimeCategory];
            tv = tv.Concat(anime);
        }

        return tv.Distinct().Order().ToList();
    }

    /// <summary>TV and anime together: the series feed of the RSS loop.</summary>
    public static IReadOnlyList<int> FeedCategories(Indexer indexer, IndexerCapabilities capabilities) => Categories(indexer, capabilities, SeriesType.Anime);

    private static List<KeyValuePair<string, string>> Ids(IndexerCapabilities capabilities, SeriesSearchCriteria criteria)
    {
        var ids = new List<KeyValuePair<string, string>>();
        if (criteria.TvdbId is > 0 && capabilities.SupportsTvParam("tvdbid"))
        {
            ids.Add(new("tvdbid", criteria.TvdbId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (criteria.TmdbId is > 0 && capabilities.SupportsTvParam("tmdbid"))
        {
            ids.Add(new("tmdbid", criteria.TmdbId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        var imdb = TorznabParser.NormalizeImdbId(criteria.ImdbId);
        if (imdb is not null && capabilities.SupportsTvParam("imdbid"))
        {
            ids.Add(new("imdbid", imdb));
        }

        return ids;
    }

    private static List<KeyValuePair<string, string>> Numbering(SeriesSearchCriteria criteria)
    {
        if (criteria.AirDate is { } date)
        {
            return
            [
                new("season", date.Year.ToString("0000", CultureInfo.InvariantCulture)),
                new("ep", $"{date.Month.ToString("00", CultureInfo.InvariantCulture)}/{date.Day.ToString("00", CultureInfo.InvariantCulture)}"),
            ];
        }

        var numbers = new List<KeyValuePair<string, string>>();
        if (criteria.SeasonNumber is { } season)
        {
            numbers.Add(new("season", season.ToString(CultureInfo.InvariantCulture)));
            if (criteria.EpisodeNumber is { } episode)
            {
                numbers.Add(new("ep", episode.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return numbers;
    }

    /// <summary>The numbers as release names write them, for indexers without TV search: <c>S01E05</c>, <c>S01</c>, <c>2024 01 05</c>.</summary>
    private static string TextSuffix(SeriesSearchCriteria criteria)
    {
        if (criteria.AirDate is { } date)
        {
            return date.ToString("yyyy MM dd", CultureInfo.InvariantCulture);
        }

        if (criteria.SeasonNumber is { } season)
        {
            return criteria.EpisodeNumber is { } episode
                ? $"S{season.ToString("00", CultureInfo.InvariantCulture)}E{episode.ToString("00", CultureInfo.InvariantCulture)}"
                : $"S{season.ToString("00", CultureInfo.InvariantCulture)}";
        }

        return string.Empty;
    }
}
