using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>A null <c>MovieId</c> unlinks the item; <c>VersionId</c>, optional, is one of that film's versions.</summary>
public sealed record LinkItemRequest(int? MovieId, int? VersionId);

/// <summary>The item after the change, and how many of its plays now carry the film.</summary>
public sealed record ItemLinkResponse(int ItemId, int? MovieId, int? VersionId, bool LinkedByHand, int Plays);

/// <summary>One film the server played and the Bigarr film it is.</summary>
public sealed record ItemFilmPair(int ItemId, int MovieId);

public sealed record LinkManyRequest(List<ItemFilmPair> Links);

/// <summary>The items linked, with their plays, and the ones skipped (linked meanwhile, gone, or not a film).</summary>
public sealed record LinkManyResponse(List<ItemLinkResponse> Linked, List<int> Skipped);

/// <summary>
/// Ties a film the server played to a Bigarr film by hand, when its ids could not (an import of a film no longer on the
/// server, a film matched to the wrong one). The link is the media server item's, not one play's: every play of the
/// item gets the film, and so do the plays to come, the tracker keeping a link chosen by hand.
/// </summary>
public sealed class ItemLinks(BigarrDbContext db)
{
    public async Task<Result<ItemLinkResponse>> LinkAsync(int itemId, LinkItemRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var item = await db.MediaServerItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
        {
            return Failure.NotFound($"Media server item {itemId}");
        }

        if (item.Kind != MediaItemKind.Movie)
        {
            return Failure.Invalid("Not a film", "Only a film the server played can be linked to a Bigarr film.");
        }

        if (request.MovieId is null && request.VersionId is not null)
        {
            return Failure.Invalid("A version needs its film");
        }

        string? versionName = null;
        if (request.MovieId is { } movieId)
        {
            if (!await db.Movies.AnyAsync(m => m.Id == movieId, ct))
            {
                return Failure.NotFound($"Movie {movieId}");
            }

            if (request.VersionId is { } versionId)
            {
                versionName = await db.MovieVersions.Where(v => v.Id == versionId && v.MovieId == movieId).Select(v => v.Name).FirstOrDefaultAsync(ct);
                if (versionName is null)
                {
                    return Failure.Invalid("Unknown version", $"Version {versionId} is not one of movie {movieId}'s.");
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var count = await ApplyAsync(item, request.MovieId, request.VersionId, versionName, ct);
        await transaction.CommitAsync(ct);
        return Result<ItemLinkResponse>.Ok(new ItemLinkResponse(item.Id, item.MovieId, item.LinkedVersionId, item.LinkedByHand, request.MovieId is null ? 0 : count));
    }

    /// <summary>
    /// Links several films the server played at once, each to its film without a version, like <see cref="LinkAsync"/>
    /// one by one, in one transaction. Only items still linked to nothing are: one linked (or unlinked by hand) since the
    /// list was made, a film gone, an episode or an unknown item is skipped and said so.
    /// </summary>
    public async Task<LinkManyResponse> LinkManyAsync(LinkManyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wanted = (request.Links ?? []).GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.First().MovieId);
        var itemIds = wanted.Keys.ToList();
        var movieIds = wanted.Values.Distinct().ToList();
        var items = await db.MediaServerItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var known = (await db.Movies.Where(m => movieIds.Contains(m.Id)).Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var linked = new List<ItemLinkResponse>();
        var skipped = new List<int>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var (itemId, movieId) in wanted)
        {
            if (!items.TryGetValue(itemId, out var item) || item.Kind != MediaItemKind.Movie || item.MovieId is not null || item.LinkedByHand || !known.Contains(movieId))
            {
                skipped.Add(itemId);
                continue;
            }

            var count = await ApplyAsync(item, movieId, null, null, ct);
            linked.Add(new ItemLinkResponse(item.Id, item.MovieId, item.LinkedVersionId, item.LinkedByHand, count));
        }

        await transaction.CommitAsync(ct);
        return new LinkManyResponse(linked, skipped);
    }

    /// <summary>Ties the item to the film (or to none) by hand and moves its plays and sessions along; returns its plays.</summary>
    private async Task<int> ApplyAsync(MediaServerItem item, int? movieId, int? versionId, string? versionName, CancellationToken ct)
    {
        item.MovieId = movieId;
        item.LinkedVersionId = versionId;
        item.LinkedByHand = true;
        await db.SaveChangesAsync(ct);

        var plays = db.Plays.Where(p => p.ItemId == item.Id);
        var sessions = db.PlaybackSessions.Where(s => s.ItemId == item.Id);
        if (versionId is { } version)
        {
            var count = await plays.ExecuteUpdateAsync(u => u.SetProperty(p => p.MovieId, movieId).SetProperty(p => p.VersionId, version).SetProperty(p => p.VersionName, versionName), ct);
            await sessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.MovieId, movieId).SetProperty(s => s.VersionId, version).SetProperty(s => s.VersionName, versionName), ct);
            return count;
        }

        // Plays already on that film keep their version; any other version belongs to the film they lose.
        await plays.Where(p => p.MovieId != movieId || movieId == null)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.MovieId, movieId).SetProperty(p => p.VersionId, (int?)null).SetProperty(p => p.VersionName, (string?)null), ct);
        await sessions.Where(s => s.MovieId != movieId || movieId == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.MovieId, movieId).SetProperty(s => s.VersionId, (int?)null).SetProperty(s => s.VersionName, (string?)null), ct);
        return await plays.CountAsync(ct);
    }
}
