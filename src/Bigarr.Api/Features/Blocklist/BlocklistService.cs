using Bigarr.Api.Features.Downloads;
using Bigarr.Api.Infrastructure;
using Bigarr.Api.Infrastructure.Persistence;
using Bigarr.Domain.Acquisition;
using Bigarr.Domain.History;
using Bigarr.Domain.Movies;
using Microsoft.EntityFrameworkCore;

namespace Bigarr.Api.Features.Blocklist;

/// <summary>
/// The blocklist: per managed version, a one-shot reading and feeding its film's default version.
/// Entries are added by a failed download or a removal from the queue, read by the decision engine, and dropped by hand.
/// </summary>
public sealed class BlocklistService(BigarrDbContext db)
{
    /// <summary>
    /// Blocklists the grab's release for every target of the grab, journaling each entry. Adds to the unit of work
    /// without saving: the caller commits with its own change. Returns the entries added.
    /// </summary>
    public async Task<List<BlocklistEntry>> BlockAsync(Grab grab, string reason, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grab);

        var targetIds = grab.Targets.Select(t => t.TargetId).ToList();
        var versions = await db.MovieVersions.AsNoTracking().Include(v => v.Movie).Where(v => targetIds.Contains(v.Id)).ToListAsync(ct);
        var movieIds = versions.Select(v => v.MovieId).Distinct().ToList();
        var defaults = await db.MovieVersions.AsNoTracking().Where(v => movieIds.Contains(v.MovieId) && v.IsDefault).ToDictionaryAsync(v => v.MovieId, ct);

        var added = new List<BlocklistEntry>();
        foreach (var version in versions)
        {
            var owner = version.Mode == VersionMode.OneShot ? defaults.GetValueOrDefault(version.MovieId) ?? version : version;
            var exists = await db.Blocklist.AnyAsync(b => b.TargetId == owner.Id
                && ((grab.ReleaseGuid != null && b.ReleaseGuid == grab.ReleaseGuid) || (grab.ReleaseGuid == null && b.SourceTitle == grab.ReleaseTitle)), ct);
            if (exists || added.Any(a => a.TargetId == owner.Id))
            {
                continue;
            }

            var entry = new BlocklistEntry
            {
                TargetId = owner.Id,
                ReleaseGuid = grab.ReleaseGuid,
                InfoHash = grab.InfoHash,
                SourceTitle = grab.ReleaseTitle,
                IndexerId = grab.IndexerId,
                IndexerName = grab.IndexerName,
                Reason = reason,
                CreatedAt = now,
            };
            db.Blocklist.Add(entry);
            added.Add(entry);
            db.History.Add(new HistoryEvent
            {
                TargetId = owner.Id,
                MovieId = version.MovieId,
                TmdbId = version.Movie.TmdbId,
                MovieTitle = version.Movie.Title,
                VersionName = owner.Name,
                EventType = HistoryEventType.Blocklisted,
                SourceTitle = grab.ReleaseTitle,
                IndexerId = grab.IndexerId,
                IndexerName = grab.IndexerName,
                Data = new Dictionary<string, string> { ["reason"] = reason, ["releaseGuid"] = grab.ReleaseGuid ?? string.Empty, ["grabId"] = grab.Id.ToString() },
                CreatedAt = now,
            });
        }

        // Series: one entry per (version, episode) pair the grab covered, read back by the episode decision.
        var episodeTargets = await db.EpisodeTargets.AsNoTracking()
            .Include(t => t.Episode)
            .Include(t => t.Version).ThenInclude(v => v.Series)
            .Where(t => targetIds.Contains(t.Id))
            .ToListAsync(ct);
        foreach (var target in episodeTargets)
        {
            var exists = await db.Blocklist.AnyAsync(b => b.TargetId == target.Id
                && ((grab.ReleaseGuid != null && b.ReleaseGuid == grab.ReleaseGuid) || (grab.ReleaseGuid == null && b.SourceTitle == grab.ReleaseTitle)), ct);
            if (exists)
            {
                continue;
            }

            var entry = new BlocklistEntry
            {
                TargetId = target.Id,
                ReleaseGuid = grab.ReleaseGuid,
                InfoHash = grab.InfoHash,
                SourceTitle = grab.ReleaseTitle,
                IndexerId = grab.IndexerId,
                IndexerName = grab.IndexerName,
                Reason = reason,
                CreatedAt = now,
            };
            db.Blocklist.Add(entry);
            added.Add(entry);
            var line = GrabService.SeriesHistory(target.Version, target, HistoryEventType.Blocklisted, grab.ReleaseTitle, grab.IndexerId, grab.IndexerName, now);
            line.Data["reason"] = reason;
            line.Data["releaseGuid"] = grab.ReleaseGuid ?? string.Empty;
            line.Data["grabId"] = grab.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.History.Add(line);
        }

        return added;
    }

    public async Task<BlocklistPageResponse> ListAsync(int? movieId, int page, int pageSize, CancellationToken ct, int? seriesId = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = from entry in db.Blocklist.AsNoTracking()
                    join version in db.MovieVersions.AsNoTracking().Include(v => v.Movie) on entry.TargetId equals version.Id into versions
                    from version in versions.DefaultIfEmpty()
                    select new { Entry = entry, Version = version };
        if (movieId is not null)
        {
            query = query.Where(x => x.Version != null && x.Version.MovieId == movieId);
        }

        if (seriesId is not null)
        {
            query = query.Where(x => db.EpisodeTargets.Any(t => t.Id == x.Entry.TargetId && t.Version.SeriesId == seriesId));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Entry.CreatedAt).ThenByDescending(x => x.Entry.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        // Series entries point at (version, episode) pairs: their show, version and episode are read for the page only.
        var pairIds = rows.Where(r => r.Version is null).Select(r => r.Entry.TargetId).ToList();
        var pairs = pairIds.Count == 0
            ? []
            : await db.EpisodeTargets.AsNoTracking().Where(t => pairIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Version.Name, t.Version.SeriesId, t.Version.Series.Title, t.Version.Series.Year, t.Version.Series.Images, t.Episode.SeasonNumber, t.Episode.EpisodeNumber })
                .ToDictionaryAsync(t => t.Id, ct);
        return new BlocklistPageResponse(
            rows.Select(x =>
            {
                var response = ToResponse(x.Entry, x.Version);
                return pairs.GetValueOrDefault(x.Entry.TargetId) is { } pair
                    ? response with
                    {
                        VersionName = pair.Name,
                        MovieYear = pair.Year,
                        PosterUrl = pair.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
                        Quality = GrabService.EpisodeQualityOf(x.Entry.SourceTitle),
                        SeriesId = pair.SeriesId,
                        SeriesTitle = pair.Title,
                        Episode = $"S{pair.SeasonNumber:00}E{pair.EpisodeNumber:00}",
                    }
                    : response;
            }).ToList(),
            total,
            page,
            pageSize);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct)
    {
        var deleted = await db.Blocklist.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Failure.NotFound("Blocklist entry") : Result<bool>.Ok(true);
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        ids.Count == 0 ? 0 : await db.Blocklist.Where(b => ids.Contains(b.Id)).ExecuteDeleteAsync(ct);

    private static BlocklistEntryResponse ToResponse(BlocklistEntry entry, MovieVersion? version) => new(
        entry.Id,
        entry.TargetId,
        version?.Name,
        version?.MovieId,
        version?.Movie.Title,
        version?.Movie.Year,
        version?.Movie.Images.FirstOrDefault(i => i.Type == ImageType.Poster)?.RemoteUrl,
        entry.SourceTitle,
        GrabService.QualityOf(entry.SourceTitle),
        entry.ReleaseGuid,
        entry.InfoHash,
        entry.IndexerId,
        entry.IndexerName,
        entry.Reason,
        entry.CreatedAt);
}
