using Bigarr.Domain.MediaServers;
using Bigarr.Domain.Movies;

namespace Bigarr.Domain.Playback;

public enum MediaItemKind
{
    Movie = 1,
    Episode = 2,
}

/// <summary>
/// What a media server played, under the server's own identity (Plex: the rating key) plus the ids that link it to a
/// catalogue: TMDB, IMDb, TVDB, and for an episode the show's ids with the season and episode numbers. One row per
/// rating key, kept when the server forgets it so old plays keep their title.
/// </summary>
public sealed class MediaServerItem
{
    public int Id { get; set; }

    public int MediaServerId { get; set; }

    public MediaServer MediaServer { get; set; } = null!;

    /// <summary>Plex's rating key, unique per server. A re-matched item gets a new one, hence a new row.</summary>
    public required string RatingKey { get; set; }

    public MediaItemKind Kind { get; set; }

    /// <summary><c>plex://movie/…</c>, or the legacy agent's guid (<c>com.plexapp.agents.imdb://…</c>).</summary>
    public string? Guid { get; set; }

    public int? TmdbId { get; set; }

    public string? ImdbId { get; set; }

    public int? TvdbId { get; set; }

    public required string Title { get; set; }

    public int? Year { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>Where the first credits marker starts: a view that reaches it counts as watched (Tautulli's marker rule).</summary>
    public long? CreditsStartMs { get; set; }

    public string? LibrarySectionId { get; set; }

    public string? LibrarySectionTitle { get; set; }

    public string? ShowRatingKey { get; set; }

    public string? ShowGuid { get; set; }

    public string? ShowTitle { get; set; }

    public int? ShowTmdbId { get; set; }

    public int? ShowTvdbId { get; set; }

    public string? ShowImdbId { get; set; }

    public string? SeasonRatingKey { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    /// <summary>The Bigarr film this item is, by TMDB or IMDb id. Null for an episode or a film Bigarr does not manage.</summary>
    public int? MovieId { get; set; }

    public Movie? Movie { get; set; }

    public DateTime FirstSeenAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
