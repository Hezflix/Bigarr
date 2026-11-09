namespace Bigarr.Domain.Tv;

/// <summary>
/// Which numbering names and matches the episodes of a series (blueprint §8). Only TVDB's default order exists for now;
/// the per-show switch to TMDB's default order or a TMDB episode group comes later. The order is a numbering layer only:
/// an episode's identity is its TVDB episode id (or TMDB id), so switching never breaks files, history or monitoring.
/// </summary>
public enum EpisodeOrder
{
    Tvdb = 1,
}
