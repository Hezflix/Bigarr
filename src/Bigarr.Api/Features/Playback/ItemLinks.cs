using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Playback;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Playback;

/// <summary>A null <c>MovieId</c> unlinks the item; <c>VersionId</c>, optional, is one of that film's versions.</summary>
public sealed record LinkItemRequest(int? MovieId, int? VersionId);

/// <summary>The item after the change, and how many of its plays now carry the film.</summary>
public sealed record ItemLinkResponse(int ItemId, int? MovieId, int? VersionId, bool LinkedByHand, int Plays);

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

        item.MovieId = request.MovieId;
        item.LinkedVersionId = request.VersionId;
        item.LinkedByHand = true;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        var plays = db.Plays.Where(p => p.ItemId == itemId);
        var sessions = db.PlaybackSessions.Where(s => s.ItemId == itemId);
        int count;
        if (request.VersionId is { } version)
        {
            count = await plays.ExecuteUpdateAsync(u => u.SetProperty(p => p.MovieId, request.MovieId).SetProperty(p => p.VersionId, version).SetProperty(p => p.VersionName, versionName), ct);
            await sessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.MovieId, request.MovieId).SetProperty(s => s.VersionId, version).SetProperty(s => s.VersionName, versionName), ct);
        }
        else
        {
            // Plays already on that film keep their version; any other version belongs to the film they lose.
            await plays.Where(p => p.MovieId != request.MovieId || request.MovieId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.MovieId, request.MovieId).SetProperty(p => p.VersionId, (int?)null).SetProperty(p => p.VersionName, (string?)null), ct);
            await sessions.Where(s => s.MovieId != request.MovieId || request.MovieId == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.MovieId, request.MovieId).SetProperty(s => s.VersionId, (int?)null).SetProperty(s => s.VersionName, (string?)null), ct);
            count = await plays.CountAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return Result<ItemLinkResponse>.Ok(new ItemLinkResponse(item.Id, item.MovieId, item.LinkedVersionId, item.LinkedByHand, request.MovieId is null ? 0 : count));
    }
}
