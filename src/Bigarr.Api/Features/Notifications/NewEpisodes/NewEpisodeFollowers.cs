using Bigarr.Api.Features.Playback;
using Bigarr.Api.Infrastructure.Auth.External;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Auth;
using Bigarr.Domain.Movies;
using Bigarr.Domain.Notifications;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Notifications.NewEpisodes;

/// <summary>Who gets a show's new episodes, and where Plex has the show.</summary>
/// <param name="Recipients">Users who watch it, want new episodes and did not opt out of the show, in id order.</param>
/// <param name="PlexShows">The show's rating key on each Plex server that played it, the most recently seen first.</param>
public sealed record ShowFollowers(IReadOnlyList<User> Recipients, IReadOnlyList<(int ServerId, string ShowRatingKey)> PlexShows)
{
    public static readonly ShowFollowers None = new([], []);
}

/// <summary>A show a user follows, as their profile lists it.</summary>
public sealed record FollowedShow(int SeriesId, string Title, int? Year, string? PosterUrl, DateTime LastWatchedAt, bool Notify);

/// <summary>
/// Subscriptions, derived and never stored: a user follows a show when they have a play of one of its episodes, on any
/// media server account of theirs. A Plex friend's account is theirs through their Plex sign-in (the same plex.tv
/// account id); the server owner's through <see cref="PlexOwners"/>. Episodes find their show with the matcher Top shows
/// uses (TVDB id, then TMDB, then IMDb). The only stored part is the opt-out of one show.
/// </summary>
public sealed class NewEpisodeFollowers(BigarrDbContext db, PlexOwners owners)
{
    public async Task<ShowFollowers> ForSeriesAsync(int seriesId, CancellationToken ct)
    {
        var series = await db.Series.AsNoTracking().Where(s => s.Id == seriesId).Select(s => new { s.TvdbId, s.TmdbId, s.ImdbId }).FirstOrDefaultAsync(ct);
        if (series is null || (series.TvdbId is null && series.TmdbId is null && series.ImdbId is null))
        {
            return ShowFollowers.None;
        }

        var candidates = await db.MediaServerItems.AsNoTracking()
            .Where(i => i.Kind == MediaItemKind.Episode
                && ((series.TvdbId != null && i.ShowTvdbId == series.TvdbId)
                    || (series.TmdbId != null && i.ShowTmdbId == series.TmdbId)
                    || (series.ImdbId != null && i.ShowImdbId == series.ImdbId)))
            .Select(i => new { i.Id, i.MediaServerId, i.ShowRatingKey, i.ShowTvdbId, i.ShowTmdbId, i.ShowImdbId, i.UpdatedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return ShowFollowers.None;
        }

        // An item that also carries another series' id (a TVDB id Bigarr has on another show) belongs to the one the matcher picks.
        var shows = candidates.Select(i => new PlaybackService.ShowIds(i.ShowTvdbId, i.ShowTmdbId, i.ShowImdbId)).Distinct().ToList();
        var artwork = await SeriesArtwork.LoadAsync(db, shows, ct);
        var items = candidates.Where(i => artwork.Match(new PlaybackService.ShowIds(i.ShowTvdbId, i.ShowTmdbId, i.ShowImdbId))?.Id == seriesId).ToList();
        var itemIds = items.Select(i => i.Id).ToList();

        var viewerIds = await db.Plays.AsNoTracking().Where(p => itemIds.Contains(p.ItemId)).Select(p => p.UserId).Distinct().ToListAsync(ct);
        var userIds = await UsersOfAsync(viewerIds, ct);
        var optedOut = await db.SeriesNotificationOptOuts.AsNoTracking().Where(o => o.SeriesId == seriesId).Select(o => o.UserId).ToListAsync(ct);
        userIds.ExceptWith(optedOut);

        var recipients = userIds.Count == 0
            ? []
            : (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).OrderBy(u => u.Id).ToListAsync(ct)).Where(Mentions.WantsNewEpisodes).ToList();

        var plexShows = items
            .Where(i => !string.IsNullOrEmpty(i.ShowRatingKey))
            .GroupBy(i => i.MediaServerId)
            .Select(g => g.OrderByDescending(i => i.UpdatedAt).First())
            .OrderByDescending(i => i.UpdatedAt)
            .Select(i => (i.MediaServerId, i.ShowRatingKey!))
            .ToList();
        return new ShowFollowers(recipients, plexShows);
    }

    /// <summary>The shows a user follows, the most recently watched first, each with whether they still want its new episodes.</summary>
    /// <returns>Null when no media server account is the user's: there is nothing to follow yet.</returns>
    public async Task<List<FollowedShow>?> ShowsOfAsync(int userId, CancellationToken ct)
    {
        var accounts = await AccountsOfAsync(userId, ct);
        if (accounts.Count == 0)
        {
            return null;
        }

        var watched = await db.Plays.AsNoTracking()
            .Where(p => accounts.Contains(p.UserId) && p.Item.Kind == MediaItemKind.Episode)
            .GroupBy(p => new { p.Item.ShowTvdbId, p.Item.ShowTmdbId, p.Item.ShowImdbId })
            .Select(g => new { g.Key.ShowTvdbId, g.Key.ShowTmdbId, g.Key.ShowImdbId, Last = g.Max(p => p.StartedAt) })
            .ToListAsync(ct);
        var artwork = await SeriesArtwork.LoadAsync(db, watched.Select(w => new PlaybackService.ShowIds(w.ShowTvdbId, w.ShowTmdbId, w.ShowImdbId)), ct);
        var lastWatched = watched
            .Select(w => (Series: artwork.Match(new PlaybackService.ShowIds(w.ShowTvdbId, w.ShowTmdbId, w.ShowImdbId)), w.Last))
            .Where(w => w.Series is not null)
            .GroupBy(w => w.Series!.Id)
            .ToDictionary(g => g.Key, g => g.Max(w => w.Last));
        if (lastWatched.Count == 0)
        {
            return [];
        }

        var ids = lastWatched.Keys.ToList();
        var series = await db.Series.AsNoTracking().Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.Title, s.Year, s.Images }).ToListAsync(ct);
        var optedOut = (await db.SeriesNotificationOptOuts.AsNoTracking().Where(o => o.UserId == userId).Select(o => o.SeriesId).ToListAsync(ct)).ToHashSet();
        return series
            .Select(s => new FollowedShow(s.Id, s.Title, s.Year, s.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl, lastWatched[s.Id], !optedOut.Contains(s.Id)))
            .OrderByDescending(s => s.LastWatchedAt)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The Bigarr users the media server accounts are.</summary>
    private async Task<HashSet<int>> UsersOfAsync(List<int> accountIds, CancellationToken ct)
    {
        if (accountIds.Count == 0)
        {
            return [];
        }

        var accounts = await db.MediaServerUsers.AsNoTracking().Where(u => accountIds.Contains(u.Id)).Select(u => new { u.MediaServerId, u.ExternalId, u.IsOwner }).ToListAsync(ct);
        var plexIds = accounts.Where(a => !a.IsOwner).Select(a => a.ExternalId).Distinct().ToList();
        var users = (await db.ExternalLogins.AsNoTracking()
                .Where(l => l.Provider == PlexIdentityProvider.ProviderKey && plexIds.Contains(l.ExternalId))
                .Select(l => l.UserId)
                .ToListAsync(ct))
            .ToHashSet();
        var ownerOf = await owners.UsersAsync(db, accounts.Where(a => a.IsOwner).Select(a => a.MediaServerId), ct);
        users.UnionWith(ownerOf.Values);
        return users;
    }

    /// <summary>The media server accounts that are the user's: their Plex account on any server, and the owner's of the servers they own.</summary>
    private async Task<List<int>> AccountsOfAsync(int userId, CancellationToken ct)
    {
        var plexIds = await db.ExternalLogins.AsNoTracking()
            .Where(l => l.UserId == userId && l.Provider == PlexIdentityProvider.ProviderKey)
            .Select(l => l.ExternalId)
            .ToListAsync(ct);
        var accounts = await db.MediaServerUsers.AsNoTracking().Select(u => new { u.Id, u.MediaServerId, u.ExternalId, u.IsOwner }).ToListAsync(ct);
        var ownerOf = await owners.UsersAsync(db, accounts.Where(a => a.IsOwner).Select(a => a.MediaServerId), ct);
        return accounts
            .Where(a => a.IsOwner ? ownerOf.GetValueOrDefault(a.MediaServerId) == userId : plexIds.Contains(a.ExternalId, StringComparer.Ordinal))
            .Select(a => a.Id)
            .ToList();
    }
}
