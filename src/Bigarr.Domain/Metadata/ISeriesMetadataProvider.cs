using Bigarr.Domain.Tv;

namespace Bigarr.Domain.Metadata;

/// <summary>
/// Source of show metadata, next to <see cref="IMetadataProvider"/> for films. The live implementation joins TVDB
/// (episode list, numbering, specials, absolute numbers) and TMDB (images, overviews, ids) through their remote ids,
/// and falls back to TMDB alone for a show TVDB lacks. A fixture provider serves the tests and keyless runs.
/// </summary>
public interface ISeriesMetadataProvider
{
    /// <summary><c>tvdb+tmdb</c>, <c>tvdb</c>, <c>tmdb</c> or <c>fixtures</c>; surfaced in the system status.</summary>
    string Name { get; }

    /// <summary>The show; with <paramref name="withEpisodes"/>, its seasons and episodes too (one or more extra requests). Null when unknown.</summary>
    Task<SeriesMetadata?> GetByTvdbIdAsync(int tvdbId, bool withEpisodes, CancellationToken cancellationToken);

    /// <summary>Same, from a TMDB id: numbered by TVDB when TMDB knows the TVDB id, by TMDB alone otherwise.</summary>
    Task<SeriesMetadata?> GetByTmdbIdAsync(int tmdbId, bool withEpisodes, CancellationToken cancellationToken);

    /// <summary>Search by name. Results carry ids and a poster, no episodes.</summary>
    Task<IReadOnlyList<SeriesMetadata>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>TMDB episode groups of a show (empty without TMDB).</summary>
    Task<IReadOnlyList<EpisodeGroupSummary>> GetEpisodeGroupsAsync(int tmdbId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EpisodeGroupSummary>>([]);

    /// <summary>
    /// Every episode of an alternative order (TMDB default or an episode group) with its numbers in that order. Null
    /// when the provider cannot give it (no TMDB, unknown group); never asked for the TVDB order.
    /// </summary>
    Task<IReadOnlyList<OrderEpisode>?> GetOrderEpisodesAsync(int tmdbId, OrderChoice order, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OrderEpisode>?>(null);
}
