using Bigarr.Domain.Metadata;
using Bigarr.Domain.Tv;
using Bigarr.Parsing;

namespace Bigarr.Api.Features.SeriesFilms;

/// <summary>How sure detection is that a special is a film.</summary>
public enum FilmConfidence
{
    /// <summary>TVDB flags the special as a movie.</summary>
    Flagged = 1,

    /// <summary>Not flagged: long, and a TMDB film has its title, year and runtime.</summary>
    Likely = 2,
}

/// <summary>Where a film sits in the show: "after season N", "before season N", or unknown.</summary>
public sealed record FilmPlacement(int? AfterSeason, int? BeforeSeason, FilmPlacementSource Source)
{
    public static readonly FilmPlacement Unknown = new(null, null, FilmPlacementSource.None);
}

/// <summary>
/// The rules that tell a film from a special (S8). Pure, so they are tested on recorded TVDB/TMDB data:
/// <list type="number">
/// <item>TVDB's <c>isMovie</c> flag on a season-0 episode makes it a film (its linked movie record gives TMDB's id);</item>
/// <item>an unflagged special of at least <see cref="MinRuntime"/> minutes is a film only when a TMDB film has the same
/// title (alone or after the show's), a year within one and a runtime within <see cref="RuntimeTolerance"/> minutes:
/// a long recap special has no such twin, so it stays a special;</item>
/// <item>placement comes from TVDB's airs-after/airs-before season when given, else from the film's date against the
/// seasons' last air dates: after the latest season that had ended by then (Scryer's <c>anime_movie_after_season</c>).</item>
/// </list>
/// </summary>
public static class SeriesFilmDetection
{
    /// <summary>Shorter unflagged specials are never looked up on TMDB.</summary>
    public const int MinRuntime = 60;

    /// <summary>How far TMDB's runtime may sit from TVDB's for a title match (cuts and credits differ).</summary>
    public const int RuntimeTolerance = 20;

    /// <summary>Copies TVDB's film fields onto the episode. A title match found earlier survives a refresh that has nothing better.</summary>
    public static void ApplyMetadata(Episode episode, EpisodeMetadata info)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(info);

        episode.IsMovie = info.IsMovie;
        episode.AirsAfterSeason = info.AirsAfterSeason;
        episode.AirsBeforeSeason = info.AirsBeforeSeason;
        episode.AirsBeforeEpisode = info.AirsBeforeEpisode;
        if (info.IsMovie)
        {
            episode.MovieMatch = SpecialMovieMatch.TvdbMovie;
            episode.MovieTmdbId = info.MovieTmdbId ?? episode.MovieTmdbId;
            episode.MovieImdbId = info.MovieImdbId ?? episode.MovieImdbId;
        }
        else if (episode.MovieMatch == SpecialMovieMatch.TvdbMovie)
        {
            // TVDB withdrew the flag: back to a plain special (a later title search may still find it).
            episode.MovieMatch = SpecialMovieMatch.None;
            episode.MovieTmdbId = null;
            episode.MovieImdbId = null;
            episode.MovieCheckedAt = null;
        }
    }

    public static bool IsCandidate(Episode episode) =>
        episode.SeasonNumber == 0 && (episode.IsMovie || episode.MovieMatch != SpecialMovieMatch.None);

    public static FilmConfidence? Confidence(Episode episode) =>
        !IsCandidate(episode) ? null : episode.IsMovie ? FilmConfidence.Flagged : FilmConfidence.Likely;

    /// <summary>An unflagged, long, never-checked special: worth one TMDB search.</summary>
    public static bool WorthTitleSearch(Episode episode) =>
        episode.SeasonNumber == 0 && !episode.IsMovie && episode.MovieMatch == SpecialMovieMatch.None
        && episode.MovieCheckedAt is null && episode.Runtime >= MinRuntime && !string.IsNullOrWhiteSpace(episode.Title);

    /// <summary>The queries a title search tries: the special's title, then the show's title followed by it.</summary>
    public static IReadOnlyList<string> TitleQueries(Episode episode, string seriesTitle)
    {
        ArgumentNullException.ThrowIfNull(episode);

        var own = StripMoviePrefix(episode.Title);
        return new[] { own, $"{seriesTitle} {own}" }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A TMDB film is the special when a title agrees, the years are within one and the runtimes close.</summary>
    public static bool Matches(Episode episode, string seriesTitle, MovieMetadata film)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(film);

        var wanted = TitleQueries(episode, seriesTitle).Select(t => t.CleanMovieTitle()).Where(t => t.Length > 0).ToHashSet();
        var titles = new[] { film.Title, film.OriginalTitle }.Concat(film.AlternativeTitles.Select(a => a.Title)).OfType<string>().Select(t => t.CleanMovieTitle());
        if (!titles.Any(wanted.Contains))
        {
            return false;
        }

        var year = episode.AirDate?.Year;
        if (year is not null && film.Year is not null && Math.Abs(year.Value - film.Year.Value) > 1)
        {
            return false;
        }

        return episode.Runtime is null || film.Runtime is null || Math.Abs(episode.Runtime.Value - film.Runtime.Value) <= RuntimeTolerance;
    }

    /// <summary>Human reasons shown next to a candidate.</summary>
    public static IReadOnlyList<string> Evidence(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        var reasons = new List<string>();
        if (episode.IsMovie)
        {
            reasons.Add("TVDB flags it as a movie");
        }

        if (episode.MovieMatch == SpecialMovieMatch.TitleAndRuntime)
        {
            reasons.Add("A TMDB film has its title, year and runtime");
        }

        if (episode.Runtime is { } runtime)
        {
            reasons.Add($"Runtime {runtime} min");
        }

        if (episode.AirsAfterSeason is not null || episode.AirsBeforeSeason is not null)
        {
            reasons.Add("TVDB places it between seasons");
        }

        return reasons;
    }

    /// <param name="episodes">Every episode of the show: the regular seasons' last air dates place a film without TVDB placement.</param>
    public static FilmPlacement Place(Episode film, IEnumerable<Episode> episodes)
    {
        ArgumentNullException.ThrowIfNull(film);
        ArgumentNullException.ThrowIfNull(episodes);

        if (film.AirsAfterSeason is > 0 and var after)
        {
            return new FilmPlacement(after, null, FilmPlacementSource.Tvdb);
        }

        if (film.AirsBeforeSeason is > 0 and var before)
        {
            return new FilmPlacement(null, before, FilmPlacementSource.Tvdb);
        }

        if (film.AirDate is not { } date)
        {
            return FilmPlacement.Unknown;
        }

        var seasons = episodes.Where(e => e.SeasonNumber > 0 && e.AirDate is not null)
            .GroupBy(e => e.SeasonNumber)
            .Select(g => (Season: g.Key, First: g.Min(e => e.AirDate!.Value), Last: g.Max(e => e.AirDate!.Value)))
            .OrderBy(s => s.Season)
            .ToList();
        if (seasons.Count == 0)
        {
            return FilmPlacement.Unknown;
        }

        var ended = seasons.Where(s => s.Last <= date).Select(s => (int?)s.Season).Max();
        if (ended is { } season)
        {
            return new FilmPlacement(season, null, FilmPlacementSource.AirDate);
        }

        return seasons[0].First > date ? new FilmPlacement(null, seasons[0].Season, FilmPlacementSource.AirDate) : FilmPlacement.Unknown;
    }

    /// <summary>"after season 2", "before season 1", or null.</summary>
    public static string? Label(int? afterSeason, int? beforeSeason) =>
        afterSeason is { } after ? $"after season {after}" : beforeSeason is { } before ? $"before season {before}" : null;

    private static string StripMoviePrefix(string title)
    {
        var trimmed = title.Trim();
        foreach (var prefix in new[] { "Movie:", "The Movie:", "Film:" })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return trimmed;
    }
}
