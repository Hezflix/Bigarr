namespace Bigarr.Domain.Metadata;

/// <summary>
/// Source of movie metadata. Two implementations live in the API's infrastructure: the real TMDB client and a
/// fixture-backed provider used in tests and whenever no TMDB key is configured.
/// </summary>
public interface IMetadataProvider
{
    /// <summary><c>tmdb</c> or <c>fixtures</c>; surfaced in the system status.</summary>
    string Name { get; }

    Task<MovieMetadata?> GetMovieAsync(int tmdbId, CancellationToken cancellationToken);

    Task<MovieMetadata?> GetMovieByImdbIdAsync(string imdbId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MovieMetadata>> SearchAsync(string query, int? year, CancellationToken cancellationToken);
}
