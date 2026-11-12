using Bigarr.Domain.Tv;
using Bigarr.Parsing.Episodes.Model;

namespace Bigarr.Api.Infrastructure.Tv;

public enum EpisodeMappingStatus
{
    Mapped = 1,
    NotParsable = 2,
    EpisodeNotFound = 3,
    Ambiguous = 4,
    WrongSeriesType = 5,
}

/// <summary>What a parsed release or file stands for in a series: zero, one or several episodes, or why not.</summary>
public sealed record EpisodeMapping(EpisodeMappingStatus Status, IReadOnlyList<Episode> Episodes, string? Detail = null)
{
    public bool IsMapped => Status == EpisodeMappingStatus.Mapped;

    /// <summary>Which numbering found the episodes (recorded on grabs and picks); meaningful when mapped.</summary>
    public EpisodeNumbering Numbering { get; init; } = EpisodeNumbering.Tvdb;

    public static EpisodeMapping Failed(EpisodeMappingStatus status, string detail) => new(status, [], detail);
}

/// <summary>The series facts the mapping needs; built from the entity or by hand in tests.</summary>
public sealed record MappableSeries(int Id, string Title, SeriesType Type, EpisodeOrder Ordering, bool UseSceneNumbering)
{
    public static MappableSeries From(Domain.Tv.Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return new MappableSeries(series.Id, series.Title, series.SeriesType, series.Ordering, series.UseSceneNumbering);
    }
}

/// <summary>
/// Maps a <see cref="ParsedEpisodeInfo"/> to episodes of one series, following Sonarr's <c>ParsingService.GetEpisodes</c>:
/// season/episode numbers in TVDB order, absolute numbers for anime, the air date for daily shows, every episode of
/// the season(s) for a pack. XEM scene numbers are used only when the source is scene-numbered (a release, or a file
/// named after an XEM scene name), against TVDB's numbers (every episode keeps them, whatever order the series shows);
/// <see cref="MapOrder"/> reads the series' alternative order instead, as a verified fallback. Where Sonarr would pick one candidate among
/// several, this reports <see cref="EpisodeMappingStatus.Ambiguous"/> instead: nothing is guessed silently.
/// Pure: no database, no HTTP. Shared by the series scan now and by search, RSS and import later.
/// </summary>
public static class EpisodeMapper
{
    /// <param name="sceneSource">The numbers come from the scene (release names): XEM numbering applies.</param>
    /// <param name="sceneSeasonNumber">
    /// The scene season the series title itself stands for, when it was found through an XEM scene name
    /// ("Show S2" → 2); null otherwise.
    /// </param>
    public static EpisodeMapping Map(
        ParsedEpisodeInfo? parsed,
        MappableSeries series,
        IReadOnlyCollection<Episode> episodes,
        bool sceneSource,
        int? sceneSeasonNumber = null)
    {
        var mapping = MapTvdb(parsed, series, episodes, sceneSource, sceneSeasonNumber);
        if (!mapping.IsMapped || parsed is null)
        {
            return mapping;
        }

        // Which numbering it was: absolute numbers (anime), XEM scene numbers (they differ from TVDB's), else TVDB.
        var useScene = sceneSource && series.UseSceneNumbering;
        var numbering = parsed.IsAbsoluteNumbering && !parsed.Special && !parsed.FullSeason
            ? EpisodeNumbering.Absolute
            : useScene && mapping.Episodes.Any(e => e.SceneSeasonNumber is not null && (e.SceneSeasonNumber != e.SeasonNumber || e.SceneEpisodeNumber != e.EpisodeNumber))
                ? EpisodeNumbering.Xem
                : EpisodeNumbering.Tvdb;
        return mapping with { Numbering = numbering };
    }

    /// <summary>
    /// The fallback on the series' alternative order: season/episode numbers, season packs and absolute
    /// numbers read in the order's numbering. Only for a series whose order is not TVDB, and only when TVDB found
    /// nothing acceptable: the caller verifies the hit. Air dates are order-independent: a daily name is not remapped.
    /// </summary>
    public static EpisodeMapping MapOrder(ParsedEpisodeInfo? parsed, MappableSeries series, IReadOnlyCollection<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);

        if (parsed is null)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "No season/episode, absolute number or air date in the name.");
        }

        if (series.Ordering == EpisodeOrder.Tvdb)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, "The series uses the TVDB order.");
        }

        if (parsed.IsDaily)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, "An air date names the same episode in every order.");
        }

        var inOrder = episodes.Where(e => e.InOrder).ToList();
        if (parsed.FullSeason)
        {
            var seasons = parsed.SeasonNumbers.ToHashSet();
            var pack = inOrder.Where(e => seasons.Contains(e.OrderSeasonNumber!.Value)).ToList();
            return pack.Count == 0
                ? EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode in season {string.Join(", ", parsed.SeasonNumbers)} of the order.")
                : new EpisodeMapping(EpisodeMappingStatus.Mapped, Ordered(pack)) { Numbering = EpisodeNumbering.Order };
        }

        var result = new List<Episode>();
        var missing = new List<string>();
        if (parsed.IsAbsoluteNumbering && parsed.AbsoluteEpisodeNumbers.Length > 0 && !parsed.Special)
        {
            foreach (var absolute in parsed.AbsoluteEpisodeNumbers)
            {
                var found = inOrder.Where(e => e.OrderAbsoluteNumber == absolute).ToList();
                if (found.Count > 1)
                {
                    return EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"Absolute {absolute} of the order is {Describe(found)}.");
                }

                if (found.Count == 0)
                {
                    missing.Add(absolute.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                result.AddRange(found);
            }
        }
        else if (parsed.SeasonNumbers.Count > 0 && parsed.EpisodeNumbers.Length > 0)
        {
            var season = parsed.SeasonNumbers[0];
            foreach (var number in parsed.EpisodeNumbers)
            {
                var found = inOrder.Where(e => e.OrderSeasonNumber == season && e.OrderEpisodeNumber == number).ToList();
                if (found.Count > 1)
                {
                    return EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"S{season:00}E{number:00} of the order is listed {found.Count} times.");
                }

                if (found.Count == 0)
                {
                    missing.Add($"S{season:00}E{number:00}");
                }

                result.AddRange(found);
            }
        }
        else
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "No episode number.");
        }

        return missing.Count > 0 || result.Count == 0
            ? EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode {string.Join(", ", missing)} in the order.")
            : new EpisodeMapping(EpisodeMappingStatus.Mapped, Ordered(result)) { Numbering = EpisodeNumbering.Order };
    }

    private static EpisodeMapping MapTvdb(
        ParsedEpisodeInfo? parsed,
        MappableSeries series,
        IReadOnlyCollection<Episode> episodes,
        bool sceneSource,
        int? sceneSeasonNumber)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(episodes);

        if (parsed is null)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "No season/episode, absolute number or air date in the name.");
        }

        // Scene numbers map to TVDB's numbers, which every episode keeps whatever the series' display order: XEM
        // serves the TVDB pass only, never the fallback on the alternative order.
        var useScene = sceneSource;
        if (!useScene)
        {
            sceneSeasonNumber = null;
        }

        int? seasonNumber = parsed.SeasonNumbers.Count > 0 ? parsed.SeasonNumbers[0] : null;
        var mappedSeason = seasonNumber;

        // Sonarr's MapSeasonNumber for an XEM name: a season-title pack takes the name's season, and a "season 1"
        // under a name that designates another scene season is that season.
        if (sceneSeasonNumber is { } nameSeason and >= 0 && (parsed.IsSeasonTitle || seasonNumber == 1))
        {
            mappedSeason = nameSeason;
        }

        // Sonarr's ValidateParsedEpisodeInfo: a date in a non-daily show's name is not an episode number.
        if (parsed.IsDaily && series.Type == SeriesType.Standard)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.WrongSeriesType, $"Daily-style name ({parsed.AirDate}) in a standard series.");
        }

        if (mappedSeason is null && !parsed.IsDaily && !parsed.IsAbsoluteNumbering)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "No season number.");
        }

        if (parsed.FullSeason)
        {
            return parsed.IsMultiSeason
                ? Found(MultiSeason(parsed, series, episodes, useScene), $"seasons {string.Join(", ", parsed.SeasonNumbers)}")
                : Found(Season(series, episodes, mappedSeason!.Value, useScene), $"season {mappedSeason}");
        }

        if (parsed.IsDaily)
        {
            return Daily(episodes, parsed.AirDate, parsed.DailyPart);
        }

        if (parsed.IsAbsoluteNumbering)
        {
            return Anime(parsed, series, episodes, useScene, sceneSeasonNumber);
        }

        if (parsed.Special && mappedSeason != 0)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"Special of season {mappedSeason} without an episode number.");
        }

        return Standard(parsed, series, episodes, mappedSeason!.Value, useScene);
    }

    private static EpisodeMapping Found(List<Episode> found, string what) =>
        found.Count == 0
            ? EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode in {what}.")
            : new EpisodeMapping(EpisodeMappingStatus.Mapped, Ordered(found));

    private static List<Episode> Season(MappableSeries series, IReadOnlyCollection<Episode> episodes, int season, bool useScene)
    {
        if (series.UseSceneNumbering && useScene)
        {
            var scene = episodes.Where(e => e.SceneSeasonNumber == season).ToList();
            if (scene.Count > 0)
            {
                return scene;
            }
        }

        return episodes.Where(e => e.SeasonNumber == season).ToList();
    }

    private static List<Episode> MultiSeason(ParsedEpisodeInfo parsed, MappableSeries series, IReadOnlyCollection<Episode> episodes, bool useScene) =>
        parsed.SeasonNumbers.SelectMany(s => Season(series, episodes, s, useScene)).Distinct().ToList();

    private static EpisodeMapping Daily(IReadOnlyCollection<Episode> episodes, string airDate, int? part)
    {
        if (!DateOnly.TryParseExact(airDate, "yyyy-MM-dd", out var date))
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, $"Unreadable air date '{airDate}'.");
        }

        var sameDay = episodes.Where(e => e.AirDate == date).OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        if (sameDay.Count == 0)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode aired on {airDate}.");
        }

        if (sameDay.Count == 1)
        {
            return new EpisodeMapping(EpisodeMappingStatus.Mapped, sameDay);
        }

        // Sonarr: "part 2" picks the second episode of the day; without a part, specials step aside.
        if (part is > 0 && part <= sameDay.Count)
        {
            return new EpisodeMapping(EpisodeMappingStatus.Mapped, [sameDay[part.Value - 1]]);
        }

        var regular = sameDay.Where(e => e.SeasonNumber > 0).ToList();
        return regular.Count == 1
            ? new EpisodeMapping(EpisodeMappingStatus.Mapped, regular)
            : EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"{sameDay.Count} episodes aired on {airDate}: {Describe(sameDay)}.");
    }

    private static EpisodeMapping Anime(ParsedEpisodeInfo parsed, MappableSeries series, IReadOnlyCollection<Episode> episodes, bool useScene, int? sceneSeasonNumber)
    {
        var result = new List<Episode>();
        var missing = new List<int>();

        foreach (var absolute in parsed.AbsoluteEpisodeNumbers)
        {
            List<Episode> found = [];

            if (parsed.Special)
            {
                found = episodes.Where(e => e.SeasonNumber == 0 && e.EpisodeNumber == absolute).ToList();
            }
            else if (useScene)
            {
                if (sceneSeasonNumber is { } nameSeason)
                {
                    // The name designates a scene season ("Show S2 - 05"): the number counts inside that season.
                    found = episodes.Where(e => e.SceneSeasonNumber == nameSeason && e.SceneEpisodeNumber == absolute).ToList();
                    if (found.Count == 0)
                    {
                        found = episodes.Where(e => e.SeasonNumber == nameSeason && e.EpisodeNumber == absolute).ToList();
                    }
                }
                else if (parsed.SeasonNumbers.FirstOrDefault() > 1 && parsed.EpisodeNumbers.Length == 0)
                {
                    var season = parsed.SeasonNumbers[0];
                    found = episodes.Where(e => e.SceneSeasonNumber == season && e.SceneEpisodeNumber == absolute).ToList();
                    if (found.Count == 0)
                    {
                        found = episodes.Where(e => e.SeasonNumber == season && e.EpisodeNumber == absolute).ToList();
                    }
                }
                else
                {
                    found = episodes.Where(e => e.SceneAbsoluteEpisodeNumber == absolute).ToList();
                    if (found.Count > 1)
                    {
                        return EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"Scene absolute {absolute} is {Describe(found)}.");
                    }
                }
            }

            if (found.Count == 0)
            {
                found = episodes.Where(e => e.AbsoluteEpisodeNumber == absolute).ToList();
            }

            if (found.Count > 1)
            {
                return EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"Absolute {absolute} is {Describe(found)}.");
            }

            if (found.Count == 0)
            {
                missing.Add(absolute);
                continue;
            }

            result.AddRange(found);
        }

        return missing.Count > 0
            ? EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode with absolute number {string.Join(", ", missing)}.")
            : new EpisodeMapping(EpisodeMappingStatus.Mapped, Ordered(result));
    }

    private static EpisodeMapping Standard(ParsedEpisodeInfo parsed, MappableSeries series, IReadOnlyCollection<Episode> episodes, int season, bool useScene)
    {
        if (parsed.EpisodeNumbers.Length == 0)
        {
            return EpisodeMapping.Failed(EpisodeMappingStatus.NotParsable, "No episode number.");
        }

        var result = new List<Episode>();
        var missing = new List<string>();

        foreach (var number in parsed.EpisodeNumbers)
        {
            List<Episode> found = [];
            if (series.UseSceneNumbering && useScene)
            {
                found = episodes.Where(e => e.SceneSeasonNumber == season && e.SceneEpisodeNumber == number).ToList();
            }

            if (found.Count == 0)
            {
                found = episodes.Where(e => e.SeasonNumber == season && e.EpisodeNumber == number).ToList();
                if (found.Count > 1)
                {
                    return EpisodeMapping.Failed(EpisodeMappingStatus.Ambiguous, $"S{season:00}E{number:00} is listed {found.Count} times.");
                }
            }

            if (found.Count == 0)
            {
                missing.Add($"S{season:00}E{number:00}");
                continue;
            }

            // A scene number can cover several TVDB episodes (a double episode aired as one): all of them.
            result.AddRange(found);
        }

        return missing.Count > 0
            ? EpisodeMapping.Failed(EpisodeMappingStatus.EpisodeNotFound, $"No episode {string.Join(", ", missing)}.")
            : new EpisodeMapping(EpisodeMappingStatus.Mapped, Ordered(result));
    }

    private static List<Episode> Ordered(IEnumerable<Episode> episodes) =>
        episodes.Distinct().OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();

    private static string Describe(IEnumerable<Episode> episodes) =>
        string.Join(", ", episodes.Take(5).Select(e => $"S{e.SeasonNumber:00}E{e.EpisodeNumber:00}"));
}
