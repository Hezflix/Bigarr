using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Metadata;

/// <summary>A TMDB episode group of a show, as listed for the order picker.</summary>
public sealed record EpisodeGroupSummary(string Id, string Name, EpisodeGroupType Type, int GroupCount, int EpisodeCount);

/// <summary>
/// One episode as an alternative order numbers it (TMDB's default order or an episode group), with what links it to a
/// TVDB episode: its TMDB episode id, its TMDB default numbers, air date and title (blueprint §8).
/// </summary>
public sealed record OrderEpisode
{
    public int? TmdbId { get; init; }

    public int SeasonNumber { get; init; }

    public int EpisodeNumber { get; init; }

    /// <summary>Running number over the regular seasons of the order (an absolute group's own numbering).</summary>
    public int? AbsoluteNumber { get; init; }

    /// <summary>TMDB's default-order numbers of the same episode (equal to the order's for the default order).</summary>
    public int? DefaultSeasonNumber { get; init; }

    public int? DefaultEpisodeNumber { get; init; }

    public string? Title { get; init; }

    public DateOnly? AirDate { get; init; }

    public int? Runtime { get; init; }
}
