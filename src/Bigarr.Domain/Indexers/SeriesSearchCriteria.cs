using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Indexers;

/// <summary>
/// What an indexer is asked for one series search, in the numbering releases use (XEM scene numbers when the episode
/// is mapped). One of: a single episode (season + episode, an air date for a daily show, an absolute number for an
/// anime), a season (season only), or the whole series (nothing but the show).
/// </summary>
public sealed record SeriesSearchCriteria(
    string Title,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    SeriesType SeriesType,
    /// <summary>Titles for the text queries after the series title: XEM scene names, then alternate titles.</summary>
    IReadOnlyList<string> AlternateTitles)
{
    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    /// <summary>Daily shows: searched by air date (<c>season=YYYY&amp;ep=MM/DD</c>).</summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>Anime: searched as "Title 05" too.</summary>
    public int? AbsoluteEpisodeNumber { get; init; }

    /// <summary>
    /// Sonarr's special search: plain text queries "&lt;series&gt; &lt;episode title&gt;", for specials releases name by title.
    /// When set, nothing else of the criteria is asked.
    /// </summary>
    public IReadOnlyList<string> SpecialQueries { get; init; } = [];

    public bool IsSeasonSearch => SeasonNumber is not null && EpisodeNumber is null && AirDate is null && AbsoluteEpisodeNumber is null;

    public bool IsSeriesSearch => SpecialQueries.Count == 0 && SeasonNumber is null && EpisodeNumber is null && AirDate is null && AbsoluteEpisodeNumber is null;

    public IEnumerable<string> AllTitles => new[] { Title }.Concat(AlternateTitles).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase);
}
